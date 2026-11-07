using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MovieRating.Models;

namespace MovieRating.Repositories;

public sealed class MongoMovieRatingRepository : IMovieRatingRepository
{
    private readonly IMongoCollection<Movie> _movies;

    public MongoMovieRatingRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _movies = database.GetCollection<Movie>("movies");
    }

    public async Task<bool> IsActiveAsync(string movieId,CancellationToken cancellationToken = default)
    {
        ValidateObjectId(movieId, nameof(movieId));

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.Eq(movie => movie.Status, "Active"));

        return await _movies.Find(filter).AnyAsync(cancellationToken);
    }

    public async Task<bool> IsEligibleForAggregationAsync(IClientSessionHandle session,string movieId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateObjectId(movieId, nameof(movieId));

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.In(movie => movie.Status,new[] { "Active", "PendingDeletion" }));

        return await _movies.Find(session, filter).AnyAsync(cancellationToken);
    }

    public async Task<bool> IsActiveAsync(
        IClientSessionHandle session,
        string movieId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateObjectId(movieId, nameof(movieId));

        var filter = Builders<Movie>.Filter.And(
            Builders<Movie>.Filter.Eq(movie => movie.Id, movieId),
            Builders<Movie>.Filter.Eq(movie => movie.Status, "Active"));

        return await _movies.Find(session, filter).AnyAsync(cancellationToken);
    }

    public async Task<RatingDeltaResult> ApplyRatingDeltaAsync(
    IClientSessionHandle session,
    string movieId,
    long sumDelta,
    long countDelta,
    DateTime updatedAtUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ValidateObjectId(movieId, nameof(movieId));

        if (countDelta is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(countDelta),"Count delta must be -1, 0, or 1.");
        }

        if (updatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The update timestamp must be UTC.",nameof(updatedAtUtc));
        }

        // No aggregate change is required, but the movie must still be eligible.
        if (sumDelta == 0 && countDelta == 0)
        {
            var eligible = await IsEligibleForAggregationAsync(session, movieId, cancellationToken);

            return eligible? RatingDeltaResult.Applied: RatingDeltaResult.MovieNotEligible;
        }

        var filterBuilder = Builders<Movie>.Filter;

        var filters = new List<FilterDefinition<Movie>>
        {
            filterBuilder.Eq(movie => movie.Id, movieId),
            filterBuilder.In(movie => movie.Status,new[] { "Active", "PendingDeletion" })
        };

        if (sumDelta < 0)
        {
            filters.Add(filterBuilder.Gte(movie => movie.RatingSum, -sumDelta));
        }

        if (countDelta < 0)
        {
            filters.Add(filterBuilder.Gte(movie => movie.RatingCount, -countDelta));
        }

        var filter = filterBuilder.And(filters);

        var classMap = BsonClassMap.LookupClassMap(typeof(Movie));

        var ratingSumField =classMap.GetMemberMap(nameof(Movie.RatingSum)).ElementName;

        var ratingCountField =classMap.GetMemberMap(nameof(Movie.RatingCount)).ElementName;

        var averageRatingField =classMap.GetMemberMap(nameof(Movie.AverageRating)).ElementName;

        var updatedAtField =classMap.GetMemberMap(nameof(Movie.UpdatedAtUtc)).ElementName;

        var newSum = new BsonDocument(
            "$add",
            new BsonArray
            {
                $"${ratingSumField}",
                sumDelta
            });

        var newCount = new BsonDocument(
            "$add",
            new BsonArray
            {
                $"${ratingCountField}",
                countDelta
            });

        var newAverage = new BsonDocument(
            "$cond",
            new BsonArray
            {
            new BsonDocument(
                "$eq",
                new BsonArray { newCount, 0 }
            ),

            BsonNull.Value,

            new BsonDocument(
                "$divide",
                new BsonArray { newSum, newCount })
            });

        var setStage = new BsonDocument(
            "$set",
            new BsonDocument
            {
            { ratingSumField, newSum },
            { ratingCountField, newCount },
            { averageRatingField, newAverage },
            { updatedAtField, updatedAtUtc }
            });

        var pipeline = PipelineDefinition<Movie, Movie>.Create(setStage);

        var update = Builders<Movie>.Update.Pipeline(pipeline);

        var result = await _movies.UpdateOneAsync(
            session,
            filter,
            update,
            cancellationToken: cancellationToken);

        if (result.MatchedCount == 1)
        {
            return RatingDeltaResult.Applied;
        }

        // The conditional update did not match. Find out why.
        var movieEligible = await IsEligibleForAggregationAsync(
            session, movieId, cancellationToken);

        return movieEligible
            ? RatingDeltaResult.AggregateInvariantViolation
            : RatingDeltaResult.MovieNotEligible;
    }

    private static void ValidateObjectId(
        string value,
        string parameterName)
    {
        if (!ObjectId.TryParse(value, out _))
        {
            throw new ArgumentException(
                "A valid MongoDB ObjectId is required.",
                parameterName);
        }
    }
}