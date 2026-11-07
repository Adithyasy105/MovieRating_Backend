using MovieRating.DTOs.Reviews;

namespace MovieRating.Services;

public interface IReviewService
{
    Task<ReviewListResponse> GetByMovieAsync(
        string movieId,
        int pageSize,
        string? cursor = null,
        CancellationToken cancellationToken = default);

    Task<ReviewOperationResult<ReviewResponse>> CreateAsync(
        string movieId,
        string userId,
        CreateReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ReviewOperationResult<ReviewResponse>> UpdateAsync(
        string movieId,
        string reviewId,
        string userId,
        UpdateReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<ReviewOperationResult<bool>> DeleteAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}