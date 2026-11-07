using MovieRating.Models;

namespace MovieRating.Aggregation;

public interface IReviewAggregationStrategy
{
    Task<ReviewMutationResult> CreateAsync(
        Review review,
        CancellationToken cancellationToken = default);

    Task<ReviewMutationResult> UpdateAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        int newRating,
        string newReviewText,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<ReviewMutationResult> DeleteAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}   