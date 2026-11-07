
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using MovieRating.Models;

namespace MovieRating.Repositories;

public sealed class MongoMovieRepository : IMovieRepository
{
    private readonly IMongoCollection<Movie> _movies;

    public MongoMovieRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _movies = database.GetCollection<Movie>("movies");
    }

    public async Task<Movie?> GetByIdAsync(string movieId,CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(movieId, out _))
        {
            return null;
        }

        var filter = Builders<Movie>.Filter.Eq(movie => movie.Id, movieId);

        return await _movies
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Movie>> GetActiveMoviesAsync(
       int skip,
       int take,
       bool isAdmin,
       CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        var filter = isAdmin
            ? Builders<Movie>.Filter.Empty
            : Builders<Movie>.Filter.Eq(movie => movie.Status, "Active");

        return await _movies
            .Find(filter)
            .SortByDescending(movie => movie.AverageRating)
            .ThenByDescending(movie => movie.RatingCount)
            .ThenBy(movie => movie.Id)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CountActiveMoviesAsync(
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        var filter = isAdmin
            ? Builders<Movie>.Filter.Empty
            : Builders<Movie>.Filter.Eq(movie => movie.Status, "Active");

        return await _movies.CountDocumentsAsync(filter,cancellationToken: cancellationToken);
    }

    public async Task CreateAsync(Movie movie,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (movie.Id is not null)
        {
            throw new ArgumentException("A movie ID must not be supplied when creating a new movie.",nameof(movie));
        }

        movie.Id ??= ObjectId.GenerateNewId().ToString();

        await _movies.InsertOneAsync(movie,cancellationToken: cancellationToken);
    }


    public async Task<MovieWriteResult> UpdateDetailsAsync(
        Movie movie,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movie);

        if (!ObjectId.TryParse(movie.Id, out _))
        {
            return MovieWriteResult.NotFoundOrInactive;
        }

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(
                existing => existing.Id, movie.Id),
            Builders<Movie>.Filter.Eq(
                existing => existing.Status, "Active"),
            Builders<Movie>.Filter.Eq(
                existing => existing.Version, expectedVersion));

        var update = Builders<Movie>.Update
            .Set(existing => existing.Name, movie.Name)
            .Set(existing => existing.NameNormalized, movie.NameNormalized)
            .Set(existing => existing.Summary, movie.Summary)
            .Set(existing => existing.ReleaseYear, movie.ReleaseYear)
            .Set(existing => existing.Cast, movie.Cast)
            .Set(existing => existing.UpdatedAtUtc, movie.UpdatedAtUtc)
            .Inc(existing => existing.Version, 1);

        var updatedMovie = await _movies.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<Movie>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (updatedMovie is not null)
        {
            return MovieWriteResult.Success;
        }

        return await ClassifyFailedWriteAsync(movie.Id,cancellationToken);
    }

    public async Task<MovieWriteResult> RequestDeletionAsync(
        IClientSessionHandle session,
        string movieId,
        string requestedByUserId,
        DateTime requestedAtUtc,
        DateTime deleteAfterUtc,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.Eq(movie => movie.Status, "Active"),
            Builders<Movie>.Filter.Eq(movie => movie.Version, expectedVersion));

        var update = Builders<Movie>.Update
            .Set(movie => movie.Status, "PendingDeletion")
            .Set(movie => movie.DeletionRequestedAtUtc, requestedAtUtc)
            .Set(movie => movie.DeleteAfterUtc, deleteAfterUtc)
            .Set(movie => movie.DeletionRequestedBy, requestedByUserId)
            .Set(movie => movie.UpdatedAtUtc, requestedAtUtc)
            .Inc(movie => movie.Version, 1);

        var updatedMovie = await _movies.FindOneAndUpdateAsync(
            session,
            filter,
            update,
            new FindOneAndUpdateOptions<Movie>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (updatedMovie is not null)
        {
            return MovieWriteResult.Success;
        }

        return await ClassifyFailedWriteAsync(session,movieId,cancellationToken);
    }

    private async Task<MovieWriteResult> ClassifyFailedWriteAsync(string movieId,CancellationToken cancellationToken)
    {
        var currentMovie = await GetByIdAsync(movieId,cancellationToken);

        if (currentMovie is null ||currentMovie.Status != "Active")
        {
            return MovieWriteResult.NotFoundOrInactive;
        }

        return MovieWriteResult.VersionConflict;
    }

    private async Task<MovieWriteResult> ClassifyFailedWriteAsync(
    IClientSessionHandle session,
    string movieId,
    CancellationToken cancellationToken)
    {
        var filter = Builders<Movie>.Filter.Eq(movie => movie.Id, movieId);

        var currentMovie = await _movies.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

        if (currentMovie is null || currentMovie.Status != "Active")
        {
            return MovieWriteResult.NotFoundOrInactive;
        }

        return MovieWriteResult.VersionConflict;
    }

    public async Task<MovieWriteResult> RestoreAsync(
    IClientSessionHandle session,
    string movieId,
    long expectedVersion,
    DateTime restoredAtUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(movieId, out _))
        {
            return MovieWriteResult.NotFoundOrInactive;
        }

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.Eq(movie => movie.Status, "PendingDeletion"),
            Builders<Movie>.Filter.Eq(movie => movie.Version, expectedVersion));

        var update = Builders<Movie>.Update
            .Set(movie => movie.Status, "Active")
            .Set(movie => movie.DeletionRequestedAtUtc, null)
            .Set(movie => movie.DeleteAfterUtc, null)
            .Set(movie => movie.DeletionRequestedBy, null)
            .Set(movie => movie.UpdatedAtUtc, restoredAtUtc)
            .Inc(movie => movie.Version, 1);

        var restoredMovie = await _movies.FindOneAndUpdateAsync(
            session,
            filter,
            update,
            new FindOneAndUpdateOptions<Movie>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (restoredMovie is not null)
        {
            return MovieWriteResult.Success;
        }

        var currentMovie = await _movies
            .Find(session, Builders<Movie>.Filter.Eq(movie => movie.Id, movieId))
            .FirstOrDefaultAsync(cancellationToken);

        if (currentMovie is null)
        {
            return MovieWriteResult.NotFoundOrInactive;
        }

        if (currentMovie.Status != "PendingDeletion")
        {
            return MovieWriteResult.NotRestorable;
        }

        return MovieWriteResult.VersionConflict;
    }

    public Task<Movie?> GetByIdAsync(
    IClientSessionHandle session,
    string movieId,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(movieId, out _))
        {
            return Task.FromResult<Movie?>(null);
        }

        var filter = Builders<Movie>.Filter.Eq(movie => movie.Id, movieId);

        return _movies.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<bool> TryStartPurgeAsync(
    IClientSessionHandle session,
    string movieId,
    long expectedVersion,
    DateTime nowUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(movieId, out _))
        {
            return false;
        }

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",nameof(nowUtc));
        }

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.Eq(movie => movie.Status,"PendingDeletion"),
            Builders<Movie>.Filter.Lte(movie => movie.DeleteAfterUtc,nowUtc),
            Builders<Movie>.Filter.Eq(movie => movie.Version,expectedVersion));

        var update = Builders<Movie>.Update
            .Set(movie => movie.Status, "Purging")
            .Set(movie => movie.UpdatedAtUtc, nowUtc)
            .Inc(movie => movie.Version, 1);

        var result = await _movies.UpdateOneAsync(
            session,
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public async Task<bool> CompletePurgeAsync(
    IClientSessionHandle session,
    string movieId,
    long expectedVersion,
    DateTime completedAtUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(movieId, out _))
            return false;

        if (expectedVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));

        if (completedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(completedAtUtc));

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.Eq(movie => movie.Status, "Purging"),
            Builders<Movie>.Filter.Eq(movie => movie.Version, expectedVersion));

        var update = Builders<Movie>.Update
            .Set(movie => movie.Status, "InActive")
            .Set(movie => movie.RatingSum, 0)
            .Set(movie => movie.RatingCount, 0)
            .Set(movie => movie.AverageRating, null)
            .Set(movie => movie.UpdatedAtUtc, completedAtUtc)
            .Inc(movie => movie.Version, 1);

        var result = await _movies.UpdateOneAsync(
            session,
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }
}
