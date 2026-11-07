
using MongoDB.Driver;
using MovieRating.Models;

namespace MovieRating.Repositories;

public interface IMovieRepository
{
    Task<Movie?> GetByIdAsync(
        string movieId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Movie>> GetActiveMoviesAsync(
        int skip,
        int take,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<long> CountActiveMoviesAsync(
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task CreateAsync(
        Movie movie,
        CancellationToken cancellationToken = default);

    Task<MovieWriteResult> UpdateDetailsAsync(
        Movie movie,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    //Task<MovieWriteResult> RequestDeletionAsync(
    //    string movieId,
    //    string requestedByUserId,
    //    DateTime requestedAtUtc,
    //    DateTime deleteAfterUtc,
    //    long expectedVersion,
    //    CancellationToken cancellationToken = default);

    Task<MovieWriteResult> RequestDeletionAsync(
    IClientSessionHandle session,
    string movieId,
    string requestedByUserId,
    DateTime requestedAtUtc,
    DateTime deleteAfterUtc,
    long expectedVersion,
    CancellationToken cancellationToken = default);


    Task<MovieWriteResult> RestoreAsync(
    IClientSessionHandle session,
    string movieId,
    long expectedVersion,
    DateTime restoredAtUtc,
    CancellationToken cancellationToken = default);

    Task<Movie?> GetByIdAsync(
    IClientSessionHandle session,
    string movieId,
    CancellationToken cancellationToken = default);

    Task<bool> TryStartPurgeAsync(
        IClientSessionHandle session,
        string movieId,
        long expectedVersion,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> CompletePurgeAsync(
    IClientSessionHandle session,
    string movieId,
    long expectedVersion,
    DateTime completedAtUtc,
    CancellationToken cancellationToken = default);
}
