
using MovieRating.DTOs.Movies;
using MovieRating.Repositories;

namespace MovieRating.Services.Movies;

public interface IMovieService
{
    Task<MovieListResponse> GetMoviesAsync(
        int page,
        int pageSize,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<MovieResponse?> GetMovieByIdAsync(
        string movieId,
        CancellationToken cancellationToken = default);

    Task<MovieResponse> CreateMovieAsync(
        CreateMovieRequest request,
        CancellationToken cancellationToken = default);

    Task<MovieUpdateResult> UpdateMovieAsync(
        string movieId,
        UpdateMovieRequest request,
        CancellationToken cancellationToken = default);

    Task<MovieDeletionResult> RequestDeletionAsync(
        string movieId,
        string requestedByUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default);

    Task<MovieDeletionResult> RestoreMovieAsync(
    string movieId,
    long expectedVersion,
    CancellationToken cancellationToken = default);
}

public sealed record MovieUpdateResult(
    MovieWriteResult Status,
    MovieResponse? Movie = null);

public sealed record MovieDeletionResult(
    MovieWriteResult Status,
    DateTime? DeleteAfterUtc = null,
    string? JobId = null);
