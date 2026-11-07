using MongoDB.Bson;
using MongoDB.Driver;
using MovieRating.Models;

namespace MovieRating.Repositories;

public interface IBackgroundJobRepository
{
    Task CreateAsync(
        IClientSessionHandle session,
        BackgroundJob job,
        CancellationToken cancellationToken = default);

    Task<BackgroundJob?> GetByIdAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    Task<bool> CancelPendingMoviePurgeAsync(
    IClientSessionHandle session,
    string movieId,
    DateTime updatedAtUtc,
    CancellationToken cancellationToken = default);

    Task<BackgroundJob?> FindNextRunnableMoviePurgeAsync(
      DateTime nowUtc,
      CancellationToken cancellationToken = default);

    Task<BackgroundJob?> GetByIdAsync(
        IClientSessionHandle session,
        string jobId,
        CancellationToken cancellationToken = default);

    Task<bool> TryClaimAsync(
        IClientSessionHandle session,
        string jobId,
        string expectedStatus,
        long expectedVersion,
        string leaseToken,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken cancellationToken = default);

    Task<BackgroundJob?> UpdatePurgeProgressAsync(
    IClientSessionHandle session,
    string jobId,
    string leaseToken,
    long expectedVersion,
    ObjectId lastProcessedReviewId,
    long deletedReviewCount,
    DateTime nowUtc,
    CancellationToken cancellationToken = default);

    Task<bool> CompleteMoviePurgeAsync(
    IClientSessionHandle session,
    string jobId,
    string leaseToken,
    long expectedVersion,
    DateTime completedAtUtc,
    CancellationToken cancellationToken = default);


}