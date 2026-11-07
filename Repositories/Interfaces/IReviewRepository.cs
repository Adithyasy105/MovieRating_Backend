using MongoDB.Bson;
using MongoDB.Driver;
using MovieRating.Models;
using MovieRating.Pagination;

namespace MovieRating.Repositories;

public interface IReviewRepository
{
    Task<ReviewPage> GetByMoviesAsync(
        string movieId,
        ReviewCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<long> CountByMovieAsync(
        string movieId,
        CancellationToken cancellationToken = default);

    Task<Review?> GetByIdAsync(
        IClientSessionHandle session,
        string movieId,
        string reviewId,
        CancellationToken cancellationToken = default);

    Task<Review?> GetByMovieAndUserAsync(
        IClientSessionHandle session,
        string movieId,
        string userId,
        CancellationToken cancellationToken = default);

    Task InsertAsync(
        IClientSessionHandle session,
        Review review,
        CancellationToken cancellationToken = default);

    Task<Review?> UpdateAsync(
        IClientSessionHandle session,
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        long dataVersion,
        int newRating,
        string newReviewText,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<Review?> DeleteAsync(
        IClientSessionHandle session,
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        CancellationToken cancellationToken = default);



    //Task<bool> MarkContributionAppliedAsync(
    //IClientSessionHandle session,
    //string movieId,
    //string reviewId,
    //long expectedVersion,
    //int appliedRating,
    //long processedVersion,
    //CancellationToken cancellationToken = default);


    // Deferred API: reads without a transaction session.
    Task<Review?> GetByIdAsync(
        string movieId,
        string reviewId,
        CancellationToken cancellationToken = default);

    Task<Review?> GetByMovieAndUserAsync(
        string movieId,
        string userId,
        CancellationToken cancellationToken = default);

    // Deferred mode: insert a review as pending.
    Task InsertDeferredAsync(
        Review review,
        CancellationToken cancellationToken = default);

    // Deferred mode: update a review and preserve its pending/lease state.
    Task<Review?> UpdateDeferredAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        long dataVersion,
        bool ratingChanged,
        int newRating,
        string newReviewText,
        DateTime? pendingSinceUtc,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    // Deferred mode: mark a review for asynchronous deletion.
    Task<Review?> MarkForDeferredDeletionAsync(
        string movieId,
        string reviewId,
        string userId,
        long expectedVersion,
        long dataVersion,
        DateTime pendingSinceUtc,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    // Worker: claim a bounded batch of pending reviews.
    Task<IReadOnlyList<Review>> ClaimPendingBatchAsync(
        int batchSize,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    // Worker: reload a review owned by the supplied lease.
    Task<Review?> GetClaimedByIdAsync(
        IClientSessionHandle session,
        string reviewId,
        string leaseToken,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    // Worker: complete an active review's latest processed version.
    Task<bool> CompleteClaimedAsync(
        IClientSessionHandle session,
        string reviewId,
        string leaseToken,
        long expectedDataVersion,
        int appliedRating,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    // Worker: physically delete a review after removing its contribution.
    Task<bool> DeleteClaimedAsync(
        IClientSessionHandle session,
        string reviewId,
        string leaseToken,
        long expectedDataVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    // Movie purge: retrieve a bounded batch of reviews.
    Task<IReadOnlyList<Review>> GetNextPurgeBatchAsync(
        IClientSessionHandle session,
        string movieId,
        ObjectId? lastProcessedReviewId,
        int batchSize,
        CancellationToken cancellationToken = default);

    // Movie purge: delete the selected batch of reviews.
    Task<long> DeletePurgeBatchAsync(
        IClientSessionHandle session,
        string movieId,
        IReadOnlyCollection<string> reviewIds,
        CancellationToken cancellationToken = default);

    Task<long> CountAllByMovieAsync(
    IClientSessionHandle session,
    string movieId,
    CancellationToken cancellationToken = default);
}