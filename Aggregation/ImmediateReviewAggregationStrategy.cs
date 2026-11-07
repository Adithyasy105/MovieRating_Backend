using MongoDB.Driver;
using MovieRating.Models;
using MovieRating.Repositories;

namespace MovieRating.Aggregation;

public sealed class ImmediateReviewAggregationStrategy : IReviewAggregationStrategy
{
    private readonly IMongoClient _mongoClient;
    private readonly IReviewRepository _reviewRepository;
    private readonly IMovieRatingRepository _movieRatingRepository;

    public ImmediateReviewAggregationStrategy(
        IMongoClient mongoClient,
        IReviewRepository reviewRepository,
        IMovieRatingRepository movieRatingRepository)
    {
        ArgumentNullException.ThrowIfNull(mongoClient);
        ArgumentNullException.ThrowIfNull(reviewRepository);
        ArgumentNullException.ThrowIfNull(movieRatingRepository);

        _mongoClient = mongoClient;
        _reviewRepository = reviewRepository;
        _movieRatingRepository = movieRatingRepository;
    }

    public async Task<ReviewMutationResult> CreateAsync(Review review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        ValidateObjectId(review.MovieId, nameof(review.MovieId));
        ValidateObjectId(review.UserId, nameof(review.UserId));
        ValidateRating(review.Rating);

        if (review.Id is not null)
        {
            throw new ArgumentException("A new review must not have an ID assigned.",nameof(review));
        }
        try
        {
            using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

            return await session.WithTransactionAsync(
                async (transactionSession, ct) =>
                {
                    var movieIsActive =await _movieRatingRepository.IsActiveAsync(transactionSession,review.MovieId,ct);

                    if (!movieIsActive)
                    {
                        return new ReviewMutationResult(ReviewMutationStatus.MovieNotFoundOrInactive);
                    }

                    var existingReview =
                        await _reviewRepository.GetByMovieAndUserAsync(
                            transactionSession,
                            review.MovieId,
                            review.UserId,
                            ct);

                    if (existingReview is not null)
                    {
                        return new ReviewMutationResult(ReviewMutationStatus.DuplicateReview);
                    }

                    var now = DateTime.UtcNow;

                    var reviewToInsert = new Review
                    {
                        MovieId = review.MovieId,
                        UserId = review.UserId,
                        UserNameSnapshot = review.UserNameSnapshot,
                        Rating = review.Rating,
                        ReviewText = review.ReviewText,

                        // Review lifecycle  for the single user deleteing his own review
                        Status = "Active",

                        // Immediate aggregation is completed in this transaction
                        Version = 1,
                        DataVersion = 1,
                        ProcessedVersion = 1,
                        AppliedRating = review.Rating,

                        // No deferred processing is pending
                        PendingSinceUtc = null,
                        ProcessingLeaseToken = null,
                        ProcessingLeaseExpiresAtUtc = null,
                        ProcessingAttemptCount = 0,

                        CreatedAtUtc = now,
                        UpdatedAtUtc = now
                    };

                    await _reviewRepository.InsertAsync(transactionSession,reviewToInsert,ct);

                    var aggregateUpdated =
                        await _movieRatingRepository.ApplyRatingDeltaAsync(
                            transactionSession,
                            reviewToInsert.MovieId,
                            sumDelta: reviewToInsert.Rating,
                            countDelta: 1,
                            updatedAtUtc: now,
                            cancellationToken: ct);

                    if(aggregateUpdated != RatingDeltaResult.Applied)
{
                        throw new InvalidOperationException($"Could not create the review contribution. " +
                            $"Movie: {reviewToInsert.MovieId}, Result: {aggregateUpdated}");
                    }

                    return new ReviewMutationResult(ReviewMutationStatus.Success,reviewToInsert);
                },
                cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The unique movieId + userId index protects against
            // two concurrent requests creating the same user's review.
            return new ReviewMutationResult(ReviewMutationStatus.DuplicateReview);
        }
    }

    public async Task<ReviewMutationResult> UpdateAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        int newRating,
        string newReviewText,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));
        ValidateRating(newRating);
        ValidateExpectedVersion(expectedVersion);
        ValidateUtc(updatedAtUtc, nameof(updatedAtUtc));

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        return await session.WithTransactionAsync(
            async (transactionSession, ct) =>
            {
                var movieIsActive =await _movieRatingRepository.IsActiveAsync(transactionSession,movieId,ct);

                if (!movieIsActive)
                {
                    return new ReviewMutationResult(ReviewMutationStatus.MovieNotFoundOrInactive);
                }

                var existingReview =await _reviewRepository.GetByIdAsync(transactionSession,movieId,reviewId,ct);

                // Return the same result for missing reviews and
                // reviews owned by another user to avoid leaking
                // review existence.
                if (existingReview is null)
                {
                    return new ReviewMutationResult(ReviewMutationStatus.ReviewNotFound);
                }

                if (existingReview.Version != expectedVersion)
                {
                    return new ReviewMutationResult(ReviewMutationStatus.VersionConflict);
                }

                var dataVersion = existingReview.Rating == newRating
                    ? existingReview.DataVersion
                    : checked(existingReview.DataVersion + 1);

                var updatedReview = await _reviewRepository.UpdateAsync(
                    transactionSession,
                    movieId,
                    reviewId,
                    userId,
                    expectedVersion,
                    dataVersion,
                    newRating,
                    newReviewText.Trim(),
                    updatedAtUtc,
                    ct);

                if (updatedReview is null)
                {
                    return new ReviewMutationResult(ReviewMutationStatus.VersionConflict);
                }

                if (existingReview.AppliedRating is not int oldAppliedRating)
                {
                    throw new InvalidOperationException("The review has no recorded aggregate contribution. " +
                        "Reconcile the data before updating this review.");
                }

                var sumDelta = (long)newRating - oldAppliedRating;

                var aggregateUpdated =
                    await _movieRatingRepository.ApplyRatingDeltaAsync(
                        transactionSession,
                        movieId,
                        sumDelta,
                        countDelta: 0,
                        updatedAtUtc,
                        ct);

                if (aggregateUpdated != RatingDeltaResult.Applied)
                {
                    throw new InvalidOperationException($"Could not update the movie aggregate. " + $"Movie: {movieId}, Result: {aggregateUpdated}");
                }

                //var contributionMarked =
                //    await _reviewRepository.MarkContributionAppliedAsync(
                //        transactionSession,
                //        movieId,
                //        reviewId,
                //        updatedReview.Version,
                //        newRating,
                //        updatedReview.DataVersion,
                //        ct);

                //if (!contributionMarked)
                //{
                //    throw new InvalidOperationException("The updated review contribution could not be recorded.");
                //}

                return new ReviewMutationResult(ReviewMutationStatus.Success,updatedReview);
            },
            cancellationToken: cancellationToken);
    }

    public async Task<ReviewMutationResult> DeleteAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));
        ValidateExpectedVersion(expectedVersion);

        using var session = await _mongoClient.StartSessionAsync(
            cancellationToken: cancellationToken);

        return await session.WithTransactionAsync(
            async (transactionSession, ct) =>
            {
                var movieIsActive =
                    await _movieRatingRepository.IsActiveAsync(
                        transactionSession,
                        movieId,
                        ct);

                if (!movieIsActive)
                {
                    return new ReviewMutationResult(
                        ReviewMutationStatus.MovieNotFoundOrInactive);
                }

                var existingReview =
                    await _reviewRepository.GetByIdAsync(
                        transactionSession,
                        movieId,
                        reviewId,
                        ct);

                if (existingReview is null ||existingReview.UserId != userId)
                {
                    return new ReviewMutationResult(ReviewMutationStatus.ReviewNotFound);
                }

                if (existingReview.Version != expectedVersion)
                {
                    return new ReviewMutationResult(ReviewMutationStatus.VersionConflict);
                }

                if (existingReview.AppliedRating is not int appliedRating)
                {
                    throw new InvalidOperationException(
                        "The review has no recorded aggregate contribution. " +
                        "Reconcile the data before deleting this review.");
                }

                var deletedReview =
                    await _reviewRepository.DeleteAsync(
                        transactionSession,
                        movieId,
                        reviewId,
                        userId,
                        expectedVersion,
                        ct);

                if (deletedReview is null)
                {
                    return new ReviewMutationResult(
                        ReviewMutationStatus.VersionConflict);
                }

                var aggregateUpdated =
                    await _movieRatingRepository.ApplyRatingDeltaAsync(
                        transactionSession,
                        movieId,
                        sumDelta: -appliedRating,
                        countDelta: -1,
                        updatedAtUtc: DateTime.UtcNow,
                        cancellationToken: ct);

                if (aggregateUpdated != RatingDeltaResult.Applied)
                {
                    throw new InvalidOperationException($"Could not delete the review contribution. " + $"Movie: {movieId}, Result: {aggregateUpdated}");
                }

                return new ReviewMutationResult(ReviewMutationStatus.Success,deletedReview);
            },
            cancellationToken: cancellationToken);
    }

    private static void ValidateRating(int rating)
    {
        if (rating is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(rating),"Rating must be between 1 and 10.");
        }
    }

    private static void ValidateExpectedVersion(long version)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(version),"Version must be at least 1.");
        }
    }

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",parameterName);
        }
    }

    private static void ValidateObjectId(string value,string parameterName)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.",parameterName);
        }
    }
}