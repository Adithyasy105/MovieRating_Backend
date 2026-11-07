using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MovieRating.Models;

namespace MovieRating.Data;

public sealed class MongoIndexInitializer : IHostedService
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoIndexInitializer> _logger;

    public MongoIndexInitializer(IMongoDatabase database,ILogger<MongoIndexInitializer> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MongoDB index initialization.");

        await CreateUserIndexesAsync(cancellationToken);
        await CreateMovieIndexesAsync(cancellationToken);
        await CreateReviewIndexesAsync(cancellationToken);
        await CreateBackgroundJobIndexesAsync(cancellationToken);

        _logger.LogInformation("MongoDB index initialization completed.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No background loop or resources need to be stopped.
        return Task.CompletedTask;
    }

    private async Task CreateUserIndexesAsync(CancellationToken cancellationToken)
    {
        var indexes = new[]
        {
            new CreateIndexModel<User>(
                     Builders<User>.IndexKeys
                    .Ascending(user => user.NormalizedEmail),
                    new CreateIndexOptions
                   {
                        Name = "ux_users_normalizedEmail",
                        Unique = true
                   })
        };

        await _database
            .GetCollection<User>("users")
            .Indexes
            .CreateManyAsync(indexes, cancellationToken);

        _logger.LogInformation("User indexes are ready.");
    }

    private async Task CreateMovieIndexesAsync(CancellationToken cancellationToken)
    {
        var movieIndexes = _database
            .GetCollection<Movie>("movies")
            .Indexes;

        var indexes = new[]
        {
            new CreateIndexModel<Movie>(
            Builders<Movie>.IndexKeys
                .Descending(movie => movie.AverageRating)
                .Descending(movie => movie.RatingCount)
                .Ascending(movie => movie.Id),
            new CreateIndexOptions<Movie>
            {
                Name = "ix_movies_active_rating_count_id",
                PartialFilterExpression =
                    Builders<Movie>.Filter.Eq(
                        movie => movie.Status,
                        "Active")
             }),

            new CreateIndexModel<Movie>(
                Builders<Movie>.IndexKeys
                    .Ascending(movie => movie.DeleteAfterUtc),
                new CreateIndexOptions<Movie>
                {
                        Name = "ix_movies_deleteAfterUtc_pending",
                        PartialFilterExpression =
                        Builders<Movie>.Filter.Eq(
                            movie => movie.Status,
                            "PendingDeletion")
                })
        };

        await movieIndexes.CreateManyAsync(indexes,cancellationToken);

        _logger.LogInformation("Movie indexes are ready.");
    }

    private async Task CreateReviewIndexesAsync(CancellationToken cancellationToken)
    {
        var reviewIndexes = _database
            .GetCollection<Review>("reviews")
            .Indexes;

        var indexes = new[]
        {
            new CreateIndexModel<Review>(
                Builders<Review>.IndexKeys
                    .Ascending(review => review.MovieId)
                    .Ascending(review => review.UserId),
                new CreateIndexOptions
                {
                    Name = "ux_reviews_movieId_userId",//unique index ux
                    Unique = true
                }),

            new CreateIndexModel<Review>(
                Builders<Review>.IndexKeys
                    .Ascending(review => review.MovieId)
                    .Descending(review => review.CreatedAtUtc)
                    .Descending(review => review.Id),
                new CreateIndexOptions
                {
                    Name =
                        "ix_reviews_movieId_createdAtUtc_id"
                }),
            new CreateIndexModel<Review>(
                Builders<Review>.IndexKeys
                    .Ascending(review => review.PendingSinceUtc)
                    .Ascending(review => review.Id),
                new CreateIndexOptions<Review>
                {   
                    Name = "ix_reviews_pendingSinceUtc_id",  //for differed exectution of reviews
                    PartialFilterExpression =
                        Builders<Review>.Filter.Type(
                            review => review.PendingSinceUtc,
                            MongoDB.Bson.BsonType.DateTime)
                }),
            new CreateIndexModel<Review>(
                Builders<Review>.IndexKeys
                    .Ascending(review => review.MovieId)
                    .Ascending(review => review.Id),
                new CreateIndexOptions
                {
                    Name = "ix_reviews_movieId_id"
                })
        };

        await reviewIndexes.CreateManyAsync(indexes,cancellationToken);

        _logger.LogInformation("Review indexes are ready.");
    }

    private async Task CreateBackgroundJobIndexesAsync(CancellationToken cancellationToken)
    {
        var jobIndexes = _database.GetCollection<BackgroundJob>("jobs").Indexes;

        var indexes = new[]
        {
        // Find pending jobs that are ready to execute.
        // Pending jobs
        new CreateIndexModel<BackgroundJob>(
            Builders<BackgroundJob>.IndexKeys
                .Ascending(job => job.NextAttemptAtUtc)
                .Ascending(job => job.CreatedAtUtc),
            new CreateIndexOptions<BackgroundJob>
        {
            Name = "ix_jobs_pending_nextAttemptAtUtc_createdAtUtc",
            PartialFilterExpression =
                Builders<BackgroundJob>.Filter.Eq(
                    job => job.Status,
                    BackgroundJobStatuses.Pending)
        }),

        // Running jobs with expired leases
        new CreateIndexModel<BackgroundJob>(
            Builders<BackgroundJob>.IndexKeys
                .Ascending(job => job.LeaseExpiresAtUtc),
        new CreateIndexOptions<BackgroundJob>
        {
            Name = "ix_jobs_running_leaseExpiresAtUtc",
            PartialFilterExpression =
                Builders<BackgroundJob>.Filter.Eq(
                    job => job.Status,
                    BackgroundJobStatuses.Running)
        })
    };

        await jobIndexes.CreateManyAsync(indexes, cancellationToken);

        _logger.LogInformation("Background job indexes are ready.");
    }
}

