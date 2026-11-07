using MovieRating.DTOs.Movies;
using MovieRating.Models;
using MovieRating.Repositories;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MovieRating.Services.Movies;

public sealed class MovieService : IMovieService
{
    private const int MaximumPageSize = 100;
    private const int MaximumPageNumber = 100_000; //similar to 100,000
    private const int DeletionGracePeriodDays = 10;

    private readonly IMovieRepository _movieRepository;
    private readonly IMongoClient _mongoClient;
    private readonly IBackgroundJobRepository _backgroundJobRepository;

    public MovieService(
        IMovieRepository movieRepository,
        IBackgroundJobRepository backgroundJobRepository,
        IMongoClient mongoClient)
    {
        ArgumentNullException.ThrowIfNull(movieRepository);
        ArgumentNullException.ThrowIfNull(backgroundJobRepository);
        ArgumentNullException.ThrowIfNull(mongoClient);

        _movieRepository = movieRepository;
        _backgroundJobRepository = backgroundJobRepository;
        _mongoClient = mongoClient;
    }

    public async Task<MovieListResponse> GetMoviesAsync(
        int page,
        int pageSize,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > MaximumPageNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                page,
                $"Page must be between 1 and {MaximumPageNumber}.");
        }

        if (pageSize < 1 || pageSize > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                pageSize,
                $"Page size must be between 1 and {MaximumPageSize}.");
        }

        var skip = checked((page - 1) * pageSize); //If an overflow occurs, C# immediately throws an OverflowException

        var movies = await _movieRepository.GetActiveMoviesAsync(
            skip,
            pageSize,
            isAdmin,
            cancellationToken);

        var totalCount = await _movieRepository.CountActiveMoviesAsync(
            isAdmin,
            cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : checked((int)(totalCount / pageSize + (totalCount % pageSize == 0 ? 0 : 1))); //if extra page is needed or not, if yes add 1 to total pages

        return new MovieListResponse
        {
            Items = movies.Select(ToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };      
    }

    public async Task<MovieResponse?> GetMovieByIdAsync(
        string movieId,
        CancellationToken cancellationToken = default)
    {
        var movie = await _movieRepository.GetByIdAsync(
            movieId,
            cancellationToken);

        if (movie is null)
        {
            return null;
        }

        return ToResponse(movie);
    }

    public async Task<MovieResponse> CreateMovieAsync(
        CreateMovieRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = DateTime.UtcNow;

        var movie = new Movie
        {
            Name = NormalizeName(request.Name),
            NameNormalized = NormalizeName(request.Name).ToLowerInvariant(),
            Summary = request.Summary.Trim(),
            ReleaseYear = request.ReleaseYear,
            Cast = NormalizeCast(request.Cast),

            // Rating fields are managed by the application.
            RatingSum = 0,
            RatingCount = 0,
            AverageRating = null,

            Status = "Active",
            DeletionRequestedAtUtc = null,
            DeleteAfterUtc = null,
            DeletionRequestedBy = null,

            Version = 1,
            ReconciledVersion = 0,

            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await _movieRepository.CreateAsync(movie,cancellationToken);

        return ToResponse(movie);
    }

    public async Task<MovieUpdateResult> UpdateMovieAsync(
        string movieId,
        UpdateMovieRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedName = NormalizeName(request.Name);

        // The repository updates only metadata fields.
        // It does not overwrite rating aggregates or lifecycle fields.
        var updatedDetails = new Movie
        {
            Id = movieId,
            Name = normalizedName,
            NameNormalized = normalizedName,
            Summary = request.Summary.Trim(),
            ReleaseYear = request.ReleaseYear,
            Cast = NormalizeCast(request.Cast),
            UpdatedAtUtc = DateTime.UtcNow
        };

        var writeResult = await _movieRepository.UpdateDetailsAsync(
            updatedDetails,
            request.Version,
            cancellationToken);

        if (writeResult != MovieWriteResult.Success)
        {
            return new MovieUpdateResult(writeResult);
        }

        var updatedMovie = await _movieRepository.GetByIdAsync(movieId,cancellationToken);

        // The movie may have entered deletion after the metadata update.
        if (updatedMovie is null || updatedMovie.Status != "Active")
        {
            return new MovieUpdateResult(MovieWriteResult.NotFoundOrInactive);
        }

        return new MovieUpdateResult(MovieWriteResult.Success,ToResponse(updatedMovie));
    }


    public async Task<MovieDeletionResult> RequestDeletionAsync(
        string movieId,
        string requestedByUserId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        // Validate request data before starting the transaction.
        if (!ObjectId.TryParse(movieId, out _))
        {
            return new MovieDeletionResult(MovieWriteResult.NotFoundOrInactive);
        }

        if (!ObjectId.TryParse(requestedByUserId, out _))
        {
            throw new ArgumentException("The requesting admin ID is invalid.",nameof(requestedByUserId));
        }

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        var requestedAtUtc = DateTime.UtcNow;
        var deleteAfterUtc = requestedAtUtc.AddDays(DeletionGracePeriodDays);

        // Create the job before starting the transaction.
        // The same job ID is retained if the transaction callback is retried.
       
        var jobId = ObjectId.GenerateNewId().ToString();

        var job = new BackgroundJob
        {
            Id = jobId,

            Type = BackgroundJobTypes.MoviePurge,
            Status = BackgroundJobStatuses.Pending,

            Payload = new BsonDocument
            {
                { "movieId", new ObjectId(movieId) }
            },

            Checkpoint = null,

            Progress = new BsonDocument
            {
                { "deletedReviewCount", 0 },
                { "batchCount", 0 }
            },

            Attempts = 0,
            NextAttemptAtUtc = deleteAfterUtc,

            LeaseToken = null,
            LeaseExpiresAtUtc = null,

            CreatedBy = requestedByUserId,
            CreatedAtUtc = requestedAtUtc,
            UpdatedAtUtc = requestedAtUtc,

            CompletedAtUtc = null,

            Version = 1
        };

        using var session = await _mongoClient.StartSessionAsync(
            cancellationToken: cancellationToken);

        var result = await session.WithTransactionAsync(
            async (transactionSession, transactionCancellationToken) =>
            {
                // Operation 1: Change movie status to PendingDeletion.
                var movieResult = await _movieRepository.RequestDeletionAsync(
                    transactionSession,
                    movieId,
                    requestedByUserId,
                    requestedAtUtc,
                    deleteAfterUtc,
                    expectedVersion,
                    transactionCancellationToken);

                if (movieResult != MovieWriteResult.Success)
                {
                    return new MovieDeletionResult(movieResult);
                }

                // Operation 2: Insert the MoviePurge job.
                await _backgroundJobRepository.CreateAsync(
                    transactionSession,
                    job,
                    transactionCancellationToken);

                return new MovieDeletionResult(
                    MovieWriteResult.Success,
                    deleteAfterUtc,
                    jobId);
            },
            cancellationToken: cancellationToken);

        return result;
    }

    public async Task<MovieDeletionResult> RestoreMovieAsync(
   string movieId,
   long expectedVersion,
   CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(movieId, out _))
        {
            return new MovieDeletionResult(MovieWriteResult.NotFoundOrInactive);
        }

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        var restoredAtUtc = DateTime.UtcNow;

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        return await session.WithTransactionAsync(
            async (transactionSession, transactionCancellationToken) =>
            {
                var movieResult = await _movieRepository.RestoreAsync(
                    transactionSession,
                    movieId,
                    expectedVersion,
                    restoredAtUtc,
                    transactionCancellationToken);

                if (movieResult != MovieWriteResult.Success)
                {
                    return new MovieDeletionResult(movieResult);
                }

                var jobCancelled =
                    await _backgroundJobRepository.CancelPendingMoviePurgeAsync(
                        transactionSession,
                        movieId,
                        restoredAtUtc,
                        transactionCancellationToken);

                if (!jobCancelled)
                {
                    // Throwing aborts the transaction.
                    // Never restore a movie without cancelling its purge job.
                    throw new InvalidOperationException("The pending movie purge job could not be found or cancelled.");
                }

                return new MovieDeletionResult(MovieWriteResult.Success);
            },
            cancellationToken: cancellationToken);
    }


    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant();
    }

    private static List<string> NormalizeCast(IEnumerable<string>? cast)
    {
        if (cast is null)
        {
            return new List<string>();
        }

        return cast
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static MovieResponse ToResponse(Movie movie)
    {
        return new MovieResponse
        {
            Id = movie.Id ?? string.Empty,
            Name = movie.Name,
            Summary = movie.Summary,
            ReleaseYear = movie.ReleaseYear,
            Cast = movie.Cast.ToArray(),

            RatingSum = movie.RatingSum,
            RatingCount = movie.RatingCount,
            AverageRating = movie.AverageRating,

            Version = movie.Version,
            CreatedAtUtc = movie.CreatedAtUtc,
            UpdatedAtUtc = movie.UpdatedAtUtc
        };
    }

}
