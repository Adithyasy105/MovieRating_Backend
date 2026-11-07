
using MongoDB.Bson;
using MovieRating.Aggregation;
using MovieRating.DTOs.Reviews;
using MovieRating.Models;
using MovieRating.Repositories;
using MovieRating.Pagination;

namespace MovieRating.Services;

public sealed class ReviewService : IReviewService
{
    private readonly IUserRepository _userRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IReviewRepository _reviewRepository;
    private readonly IReviewAggregationStrategy _aggregationStrategy;

    public ReviewService(
        IUserRepository userRepository,
        IMovieRepository movieRepository,
        IReviewRepository reviewRepository,
        IReviewAggregationStrategy aggregationStrategy)
    {
        _userRepository = userRepository;
        _movieRepository = movieRepository;
        _reviewRepository = reviewRepository;
        _aggregationStrategy = aggregationStrategy;
    }

    public async Task<ReviewListResponse> GetByMovieAsync(string movieId,int pageSize,string? cursor = null,
    CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));

        if (pageSize < 1 || pageSize > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize),"Page size must be between 1 and 100.");
        }

        var movie = await _movieRepository.GetByIdAsync(movieId, cancellationToken);

        if (movie is null || movie.Status != "Active")
        {
            throw new KeyNotFoundException("Movie not found.");
        }

        var decodedCursor = string.IsNullOrWhiteSpace(cursor) ? null: ReviewCursorCodec.Decode(cursor);

        var page = await _reviewRepository.GetByMoviesAsync(movieId,decodedCursor,pageSize,cancellationToken);

        string? nextCursor = null;

        if (page.HasMore && page.Items.Count > 0)
        {
            var lastReview = page.Items[^1];

            nextCursor = ReviewCursorCodec.Encode(
                new ReviewCursor(
                    lastReview.CreatedAtUtc,
                    lastReview.Id
                    ?? throw new InvalidOperationException("Persisted review has no ID.")));
        }

        return new ReviewListResponse
        {
            Items = page.Items.Select(MapToResponse).ToArray(), //ToArray allocate the exact amount of memory needed for the fixed number of elements. 
            PageSize = pageSize,
            HasMore = page.HasMore,
            NextCursor = nextCursor
        };
    }

    public async Task<ReviewOperationResult<ReviewResponse>> CreateAsync(
        string movieId,
        string userId,
        CreateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(userId, nameof(userId));

        ValidateReviewContent(request.Rating, request.ReviewText);

        var movie = await _movieRepository.GetByIdAsync(movieId, cancellationToken);

        if (movie is null || movie.Status != "Active")
        {
            return new ReviewOperationResult<ReviewResponse>(ReviewOperationStatus.MovieNotFoundOrInactive);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);

        if (user is null || user.Status != "Active")
        {
            throw new UnauthorizedAccessException("The user account is unavailable.");
        }

        var now = DateTime.UtcNow;

        var review = new Review
        {
            MovieId = movieId,
            UserId = userId,
            UserNameSnapshot = user.DisplayName,
            Rating = request.Rating,
            ReviewText = request.ReviewText.Trim(),
            //Status = "Active",

            //Version = 1,
            //DataVersion = 1,
            //ProcessedVersion = 1,
            //AppliedRating = request.Rating,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var result = await _aggregationStrategy.CreateAsync(review, cancellationToken);

        return MapMutationResult(result);
    }

    public async Task<ReviewOperationResult<ReviewResponse>> UpdateAsync(
        string movieId,
        string reviewId,
        string userId,
        UpdateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateObjectId(movieId, nameof(movieId));
        ValidateObjectId(reviewId, nameof(reviewId));
        ValidateObjectId(userId, nameof(userId));

        ValidateReviewContent(request.Rating, request.ReviewText);

        var result = await _aggregationStrategy.UpdateAsync(
            movieId,
            reviewId,
            userId,
            request.Version,
            request.Rating,
            request.ReviewText.Trim(),
            DateTime.UtcNow,
            cancellationToken);

        return MapMutationResult(result);
    }

    public async Task<ReviewOperationResult<bool>> DeleteAsync(
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
            throw new ArgumentOutOfRangeException(nameof(expectedVersion),"Expected version must be at least 1.");
        }

        var result = await _aggregationStrategy.DeleteAsync(
            movieId,
            reviewId,
            userId,
            expectedVersion,
            cancellationToken);

        return result.Status switch
        {
            ReviewMutationStatus.Success =>new ReviewOperationResult<bool>(ReviewOperationStatus.Success, true),

            ReviewMutationStatus.MovieNotFoundOrInactive =>new ReviewOperationResult<bool>(ReviewOperationStatus.MovieNotFoundOrInactive),

            ReviewMutationStatus.ReviewNotFound =>new ReviewOperationResult<bool>(ReviewOperationStatus.ReviewNotFound),

            ReviewMutationStatus.VersionConflict =>new ReviewOperationResult<bool>(ReviewOperationStatus.VersionConflict),

            ReviewMutationStatus.DuplicateReview =>
                throw new InvalidOperationException("Unexpected duplicate-review result during deletion."),

            _ => throw new InvalidOperationException("Unknown review mutation status.")
        };
    }

    private static ReviewOperationResult<ReviewResponse> MapMutationResult(ReviewMutationResult result)
    {
        return result.Status switch
        {
            ReviewMutationStatus.Success when result.Review is not null =>
                new ReviewOperationResult<ReviewResponse>(ReviewOperationStatus.Success,MapToResponse(result.Review)),

            ReviewMutationStatus.Success =>
                throw new InvalidOperationException("A successful review mutation returned no review."),

            ReviewMutationStatus.MovieNotFoundOrInactive =>
                new ReviewOperationResult<ReviewResponse>(
                    ReviewOperationStatus.MovieNotFoundOrInactive),

            ReviewMutationStatus.ReviewNotFound =>
                new ReviewOperationResult<ReviewResponse>(
                    ReviewOperationStatus.ReviewNotFound),

            ReviewMutationStatus.VersionConflict =>
                new ReviewOperationResult<ReviewResponse>(
                    ReviewOperationStatus.VersionConflict),

            ReviewMutationStatus.DuplicateReview =>
                new ReviewOperationResult<ReviewResponse>(
                    ReviewOperationStatus.DuplicateReview),

            _ => throw new InvalidOperationException("Unknown review mutation status.")
        };
    }

    private static ReviewResponse MapToResponse(Review review)
    {
        return new ReviewResponse
        {
            Id = review.Id?? throw new InvalidOperationException("Persisted review has no ID."),
            MovieId = review.MovieId,
            UserName = review.UserNameSnapshot,
            Rating = review.Rating,
            ReviewText = review.ReviewText,
            Version = review.Version,
            CreatedAtUtc = review.CreatedAtUtc,
            UpdatedAtUtc = review.UpdatedAtUtc
        };
    }

    private static void ValidateObjectId(string id,string parameterName)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            throw new ArgumentException("A valid MongoDB ObjectId is required.",parameterName);
        }
    }

    private static void ValidateReviewContent(int rating,string reviewText)
    {
        if (rating is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(rating), "Rating must be between 1 and 10.");
        }

        if (string.IsNullOrWhiteSpace(reviewText))
        {
            throw new ArgumentException("Review text cannot be empty.",nameof(reviewText));
        }

        if (reviewText.Trim().Length > 2000)
        {
            throw new ArgumentException("Review text cannot exceed 2000 characters.",nameof(reviewText));
        }
    }
}
