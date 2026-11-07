
using MongoDB.Driver;
using MovieRating.Models;
using MovieRating.Repositories;

namespace MovieRating.BackgroundJobs.DeferredReviewAggregation;

public sealed class DeferredReviewProcessor
{
    private readonly IMongoClient _mongoClient;
    private readonly IReviewRepository _reviewRepository;
    private readonly IMovieRatingRepository _movieRatingRepository;
    private readonly ILogger<DeferredReviewProcessor> _logger;

    public DeferredReviewProcessor(
        IMongoClient mongoClient,
        IReviewRepository reviewRepository,
        IMovieRatingRepository movieRatingRepository,
        ILogger<DeferredReviewProcessor> logger)
    {
        _mongoClient = mongoClient?? throw new ArgumentNullException(nameof(mongoClient));

        _reviewRepository = reviewRepository?? throw new ArgumentNullException(nameof(reviewRepository));

        _movieRatingRepository = movieRatingRepository?? throw new ArgumentNullException(nameof(movieRatingRepository));

        _logger = logger?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DeferredReviewProcessingStatus> ProcessAsync(string reviewId,string leaseToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewId);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        var transactionOptions = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,//Read a consistent snapshot of database data during the transaction
            readPreference: ReadPreference.Primary,//Read from the MongoDB primary node.
            writeConcern: WriteConcern.WMajority); //majority of voting data-bearing members.

        try
        {
            return await session.WithTransactionAsync(
                async (transactionSession, transactionCancellationToken) =>
                {
                    // 1. Reload the review using the current lease.
                    var nowUtc = DateTime.UtcNow;

                    var review = await _reviewRepository.GetClaimedByIdAsync(
                        transactionSession,
                        reviewId,
                        leaseToken,
                        nowUtc,
                        transactionCancellationToken);

                    if (review is null)
                    {
                        return DeferredReviewProcessingStatus.LeaseLost;
                    }

                    // 2. Verify that this review actually has pending work.
                    if (review.DataVersion < review.ProcessedVersion)
                    {
                        throw new InvalidOperationException($"Review {review.Id} has an invalid aggregation version.");
                    }

                    // 3. Check whether the movie can still receive aggregation changes.
                    var movieEligible =
                        await _movieRatingRepository.IsEligibleForAggregationAsync(
                            transactionSession,
                            review.MovieId,
                            transactionCancellationToken);

                    if (!movieEligible)
                    {
                        return DeferredReviewProcessingStatus.MovieNotEligible;
                    }

                    // 4. Process the review according to its status.
                    if (review.Status == "Active")
                    {
                        return await ProcessActiveReviewAsync(
                            transactionSession,
                            review,
                            leaseToken,
                            transactionCancellationToken);
                    }

                    if (review.Status == "PendingDeletion")
                    {
                        return await ProcessDeletedReviewAsync(
                            transactionSession,
                            review,
                            leaseToken,
                            transactionCancellationToken);
                    }

                    throw new InvalidOperationException($"Review {review.Id} has unsupported status {review.Status}.");
                },
                transactionOptions,
                cancellationToken);
        }
        catch (DeferredReviewConcurrencyException)
        {
            _logger.LogInformation("Review {ReviewId} could not be completed because its state changed.",reviewId);

            return DeferredReviewProcessingStatus.ConcurrencyConflict;
        }
    }

    private async Task<DeferredReviewProcessingStatus> ProcessActiveReviewAsync(
        IClientSessionHandle session,
        Review review,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        // A pending active review should have a newer data version.
        if (review.DataVersion <= review.ProcessedVersion)
        {
            throw new InvalidOperationException($"Review {review.Id} is queued but has no unprocessed data version. OR processed version is invalid");
        }

        if (review.AppliedRating is < 1 or > 10)
        {
            throw new InvalidOperationException($"Review {review.Id} has an invalid applied rating.");
        }


        var previousRating = review.AppliedRating ?? 0;

        // Calculate the difference between the new and previous rating.
        var sumDelta = (long)review.Rating - previousRating;

        // A new review adds one to the count.
        // An existing review keeps the same count.
        var countDelta = review.AppliedRating is null ? 1L : 0L; //L means long type

        // 1. Apply the rating change to the movie.
        var aggregateResult =
        await _movieRatingRepository.ApplyRatingDeltaAsync(
            session,
            review.MovieId,
            sumDelta,
            countDelta,
            DateTime.UtcNow,
            cancellationToken);

        switch (aggregateResult)
        {
            case RatingDeltaResult.Applied:
                break;

            case RatingDeltaResult.MovieNotEligible:
                return DeferredReviewProcessingStatus.MovieNotEligible;

            case RatingDeltaResult.AggregateInvariantViolation:
                throw new InvalidOperationException($"Movie {review.MovieId} has an inconsistent rating aggregate.");

            default:
                throw new ArgumentOutOfRangeException(nameof(aggregateResult));
        }

        // 2. Complete the review using its original data version.
        // Recheck lease ownership immediately before the conditional write.
        var completed = await _reviewRepository.CompleteClaimedAsync(
            session,
            review.Id!,
            leaseToken,
            review.DataVersion,
            review.Rating,
            DateTime.UtcNow,
            cancellationToken);

        if (!completed)
        {
            // Throwing aborts the transaction, including the movie update.
            throw new DeferredReviewConcurrencyException();
        }

        return DeferredReviewProcessingStatus.Processed;
    }

    private async Task<DeferredReviewProcessingStatus> ProcessDeletedReviewAsync(
        IClientSessionHandle session,
        Review review,
        string leaseToken,
        CancellationToken cancellationToken)
    {
        if (review.DataVersion <= review.ProcessedVersion) //review deletion must have always unprocessed data logiclay 
        {
            throw new InvalidOperationException($"Deleted review {review.Id} has no pending deletion version.");
        }

       

        if (review.AppliedRating is < 1 or > 10)
        {
            throw new InvalidOperationException($"Deleted review {review.Id} has an invalid applied rating.");
        }


        long sumDelta = 0;
        long countDelta = 0;

        if (review.AppliedRating.HasValue)
        {
            sumDelta = -review.AppliedRating.Value;
            countDelta = -1;
        }

        // 1. Remove the old contribution from the movie.
        var aggregateResult =
        await _movieRatingRepository.ApplyRatingDeltaAsync(
            session,
            review.MovieId,
            sumDelta,
            countDelta,
            DateTime.UtcNow,
            cancellationToken);

        switch (aggregateResult)
        {
            case RatingDeltaResult.Applied:
                break;

            case RatingDeltaResult.MovieNotEligible:
                return DeferredReviewProcessingStatus.MovieNotEligible;

            case RatingDeltaResult.AggregateInvariantViolation:
                throw new InvalidOperationException($"Movie {review.MovieId} has an inconsistent rating aggregate.");

            default:
                throw new ArgumentOutOfRangeException(nameof(aggregateResult));
        }

        // 2. Physically delete the review in the same transaction.
        var deleted = await _reviewRepository.DeleteClaimedAsync(
            session,
            review.Id!,
            leaseToken,
            review.DataVersion,
            DateTime.UtcNow,
            cancellationToken);

        if (!deleted)
        {
            // Roll back the movie aggregate change as well.
            throw new DeferredReviewConcurrencyException();
        }

        return DeferredReviewProcessingStatus.Processed;
    }

    // An exception thrown when a deferred review cannot be processed due to a concurrency issue.
    private sealed class DeferredReviewConcurrencyException : Exception
    {
        public DeferredReviewConcurrencyException(): base("The review state changed during processing.")
        {
        }
    }
    //"The database state changed while I was processing this review. Do not commit any of the changes made in this transaction." about means mangodb will rollback the transaction and not commit any changes made in this transaction.
}
