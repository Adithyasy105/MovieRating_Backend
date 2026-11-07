using MongoDB.Bson;
using MongoDB.Driver;
using MovieRating.Models;
using MovieRating.Repositories;

namespace MovieRating.Aggregation;

public sealed class DeferredReviewAggregationStrategy : IReviewAggregationStrategy
{
    private readonly IReviewRepository _reviewRepository;
    private readonly IMovieRatingRepository _movieRatingRepository;

    public DeferredReviewAggregationStrategy(IReviewRepository reviewRepository,IMovieRatingRepository movieRatingRepository)
    {
        _reviewRepository = reviewRepository?? throw new ArgumentNullException(nameof(reviewRepository));

        _movieRatingRepository = movieRatingRepository?? throw new ArgumentNullException(nameof(movieRatingRepository));
    }

    //create review in deferred mode
    public async Task<ReviewMutationResult> CreateAsync(Review review,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        ValidateObjectId(review.MovieId, nameof(review.MovieId));
        ValidateObjectId(review.UserId, nameof(review.UserId));

        if (review.Rating is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(review.Rating),"Rating must be between 1 and 10.");
        }

        if (string.IsNullOrWhiteSpace(review.ReviewText))
        {
            throw new ArgumentException("Review text cannot be empty.",nameof(review.ReviewText));
        }

        ValidateUtc(review.CreatedAtUtc, nameof(review.CreatedAtUtc));

        // Only active movies accept new reviews.
        var isMovieActive = await _movieRatingRepository.IsActiveAsync(review.MovieId,cancellationToken);

        if (!isMovieActive)
        {
            return new ReviewMutationResult(ReviewMutationStatus.MovieNotFoundOrInactive);
        }

        // This pre-check gives a friendly duplicate response.
        // The unique index is still the final concurrency protection.
        var existingReview =await _reviewRepository.GetByMovieAndUserAsync(review.MovieId,review.UserId,cancellationToken);

        if (existingReview is not null)
        {
            return new ReviewMutationResult(ReviewMutationStatus.DuplicateReview);
        }

        // Initialize the review for deferred aggregation.
        review.ReviewText = review.ReviewText.Trim();
        review.Status = "Active";
        review.DataVersion = 1;
        review.ProcessedVersion = 0;
        review.AppliedRating = null;

        review.PendingSinceUtc = review.CreatedAtUtc;

        review.ProcessingLeaseToken = null;
        review.ProcessingLeaseExpiresAtUtc = null;
        review.ProcessingAttemptCount = 0;
        review.Version = 1;

        try
        {
            await _reviewRepository.InsertDeferredAsync(review,cancellationToken);

            return new ReviewMutationResult(ReviewMutationStatus.Success,review);
        }
        catch (MongoWriteException exception)
            when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return new ReviewMutationResult(ReviewMutationStatus.DuplicateReview);
        }
    }

    //update review in deferred mode
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

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (newRating is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(newRating),"Rating must be between 1 and 10.");
        }

        if (string.IsNullOrWhiteSpace(newReviewText))
        {
            throw new ArgumentException("Review text cannot be empty.",nameof(newReviewText));
        }

        ValidateUtc(updatedAtUtc, nameof(updatedAtUtc));

        // API updates are allowed only for active movies.
        var isMovieActive = await _movieRatingRepository.IsActiveAsync(movieId,cancellationToken);

        if (!isMovieActive)
        {
            return new ReviewMutationResult(ReviewMutationStatus.MovieNotFoundOrInactive);
        }

        var existingReview = await _reviewRepository.GetByIdAsync(movieId,reviewId,cancellationToken);

        if (existingReview is null ||!string.Equals(existingReview.UserId,userId,StringComparison.Ordinal))
        {
            return new ReviewMutationResult(ReviewMutationStatus.ReviewNotFound);
        }

        if (existingReview.Status != "Active")
        {
            return new ReviewMutationResult(ReviewMutationStatus.ReviewNotFound);
        }

        if (existingReview.Version != expectedVersion)
        {
            return new ReviewMutationResult(ReviewMutationStatus.VersionConflict);
        }

        var ratingChanged = existingReview.Rating != newRating;

        var newDataVersion = ratingChanged? checked(existingReview.DataVersion + 1) : existingReview.DataVersion;

        DateTime? pendingSinceUtc = null;

        if (ratingChanged)
        {
            pendingSinceUtc = existingReview.PendingSinceUtc ?? updatedAtUtc;
        }

        // The repository performs an atomic conditional update.
        // It checks the expected version again, so a concurrent
        // update cannot be overwritten by this request.
        var updatedReview =await _reviewRepository.UpdateDeferredAsync(
                movieId,
                reviewId,
                userId,
                expectedVersion,
                newDataVersion,
                ratingChanged,
                newRating,
                newReviewText.Trim(),
                pendingSinceUtc,
                updatedAtUtc,
                cancellationToken);

        if (updatedReview is null)
        {
            return new ReviewMutationResult(ReviewMutationStatus.VersionConflict);
        }

        // The movie aggregate is intentionally not updated here.
        // The deferred worker will process the latest review version.
        return new ReviewMutationResult(ReviewMutationStatus.Success,updatedReview);
    }

    //delete review in deferred mode
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

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        var nowUtc = DateTime.UtcNow;

        // API deletion is allowed only for active movies.
        var isMovieActive = await _movieRatingRepository.IsActiveAsync(movieId,cancellationToken);

        if (!isMovieActive)
        {
            return new ReviewMutationResult(ReviewMutationStatus.MovieNotFoundOrInactive);
        }

        var existingReview = await _reviewRepository.GetByIdAsync(movieId,reviewId,cancellationToken);

        if (existingReview is null ||!string.Equals(existingReview.UserId,userId,StringComparison.Ordinal))
        {
            return new ReviewMutationResult(ReviewMutationStatus.ReviewNotFound);
        }

        if (existingReview.Status != "Active")
        {
            return new ReviewMutationResult(ReviewMutationStatus.ReviewNotFound);
        }

        if (existingReview.Version != expectedVersion)
        {
            return new ReviewMutationResult(ReviewMutationStatus.VersionConflict);
        }

        var newDataVersion = checked(existingReview.DataVersion + 1);

        // Preserve the queue position if the review was already pending.
        var pendingSinceUtc =existingReview.PendingSinceUtc ?? nowUtc;

        // Mark the review for deferred physical deletion.
        // The repository conditionally checks the expected version.
        var markedReview =await _reviewRepository.MarkForDeferredDeletionAsync(
                movieId,
                reviewId,
                userId,
                expectedVersion,
                newDataVersion,
                pendingSinceUtc,
                nowUtc,
                cancellationToken);

        if (markedReview is null)
        {
            return new ReviewMutationResult(
                ReviewMutationStatus.VersionConflict);
        }

        // Do not physically delete the review here.
        // The worker will remove its applied rating, if any,
        // and physically delete the review in one transaction.
        return new ReviewMutationResult(ReviewMutationStatus.Success,markedReview);
    }

    private static void ValidateObjectId(string? value,string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||!ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.",parameterName);
        }
    }

    private static void ValidateUtc(DateTime value,string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",parameterName);
        }
    }
}