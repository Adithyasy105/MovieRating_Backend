
using MongoDB.Bson;
using MongoDB.Driver;
using MovieRating.Models;

namespace MovieRating.Repositories;

public sealed class MongoBackgroundJobRepository : IBackgroundJobRepository
{
    private readonly IMongoCollection<BackgroundJob> _jobs;

    public MongoBackgroundJobRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _jobs = database.GetCollection<BackgroundJob>("jobs");
    }

    public async Task CreateAsync(
        IClientSessionHandle session,
        BackgroundJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(job);


        if (!ObjectId.TryParse(job.Id, out _))
        {
            throw new ArgumentException("A valid job ID must be assigned before inserting the job.",nameof(job));
        }


        if (!ObjectId.TryParse(job.CreatedBy, out _))
        {
            throw new ArgumentException("CreatedBy must be a valid MongoDB ObjectId.",nameof(job));
        }

        if (string.IsNullOrWhiteSpace(job.Type))
        {
            throw new ArgumentException("Job type is required.",nameof(job));
        }

        if (job.Payload is null)
        {
            throw new ArgumentException("Job payload cannot be null.",nameof(job));
        }

        await _jobs.InsertOneAsync(session,job,cancellationToken: cancellationToken);
    }

    public async Task<BackgroundJob?> GetByIdAsync(string jobId,CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(jobId, out _))
        {
            return null;
        }

        var filter = Builders<BackgroundJob>.Filter.Eq(job => job.Id, jobId);

        return await _jobs.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CancelPendingMoviePurgeAsync(
    IClientSessionHandle session,
    string movieId,
    DateTime updatedAtUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(movieId, out var movieObjectId))
        {
            return false;
        }

        var filter = Builders<BackgroundJob>.Filter.And(
            Builders<BackgroundJob>.Filter.Eq(job => job.Type,BackgroundJobTypes.MoviePurge),
            Builders<BackgroundJob>.Filter.Eq(job => job.Status,BackgroundJobStatuses.Pending),
            Builders<BackgroundJob>.Filter.Eq("Payload.movieId",movieObjectId));

        var update = Builders<BackgroundJob>.Update
            .Set(job => job.Status, BackgroundJobStatuses.Cancelled)
            .Set(job => job.UpdatedAtUtc, updatedAtUtc)
            .Set(job => job.NextAttemptAtUtc, null)
            .Inc(job => job.Version, 1);

        var result = await _jobs.UpdateOneAsync(session,filter,update,cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }

    public Task<BackgroundJob?> FindNextRunnableMoviePurgeAsync(
    DateTime nowUtc,
    CancellationToken cancellationToken = default)
    {
        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The timestamp must be UTC.",nameof(nowUtc));
        }

        var filterBuilder = Builders<BackgroundJob>.Filter;

        var pendingDue = filterBuilder.And(
            filterBuilder.Eq(job => job.Status, BackgroundJobStatuses.Pending),
            filterBuilder.Lte(job => job.NextAttemptAtUtc, nowUtc));

        var expiredRunning = filterBuilder.And(
            filterBuilder.Eq(job => job.Status, BackgroundJobStatuses.Running),
            filterBuilder.Lte(job => job.LeaseExpiresAtUtc, nowUtc));

        var filter = filterBuilder.And(
            filterBuilder.Eq(job => job.Type, BackgroundJobTypes.MoviePurge),
            filterBuilder.Or(pendingDue, expiredRunning));

        return _jobs.Find(filter).SortBy(job => job.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<BackgroundJob?> GetByIdAsync(
    IClientSessionHandle session,
    string jobId,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(jobId, out _))
        {
            return Task.FromResult<BackgroundJob?>(null);
        }

        var filter = Builders<BackgroundJob>.Filter.Eq(job => job.Id,jobId);

        return _jobs.Find(session, filter).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<bool> TryClaimAsync(
    IClientSessionHandle session,
    string jobId,
    string expectedStatus,
    long expectedVersion,
    string leaseToken,
    DateTime nowUtc,
    DateTime leaseExpiresAtUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!ObjectId.TryParse(jobId, out _))
        {
            return false;
        }

        if (expectedStatus != BackgroundJobStatuses.Pending && expectedStatus != BackgroundJobStatuses.Running)
        {
            throw new ArgumentException("Only Pending or Running jobs can be claimed.",nameof(expectedStatus));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        }

        if (nowUtc.Kind != DateTimeKind.Utc ||leaseExpiresAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamps must be UTC.");
        }

        if (leaseExpiresAtUtc <= nowUtc)
        {
            throw new ArgumentException("Lease expiry must be later than the current time.",nameof(leaseExpiresAtUtc));
        }

        var filterBuilder = Builders<BackgroundJob>.Filter;

        var filters = new List<FilterDefinition<BackgroundJob>>
        {
            filterBuilder.Eq(job => job.Id, jobId),
            filterBuilder.Eq(job => job.Type, BackgroundJobTypes.MoviePurge),
            filterBuilder.Eq(job => job.Status, expectedStatus),
            filterBuilder.Eq(job => job.Version, expectedVersion)
        };

        if (expectedStatus == BackgroundJobStatuses.Pending)
        {
            filters.Add(filterBuilder.Lte(job => job.NextAttemptAtUtc, nowUtc));
        }
        else
        {
            filters.Add(filterBuilder.Lte(job => job.LeaseExpiresAtUtc, nowUtc));
        }

        var filter = filterBuilder.And(filters);

        var update = Builders<BackgroundJob>.Update
            .Set(job => job.Status, BackgroundJobStatuses.Running)
            .Set(job => job.LeaseToken, leaseToken)
            .Set(job => job.LeaseExpiresAtUtc, leaseExpiresAtUtc)
            .Set(job => job.NextAttemptAtUtc, null)
            .Set(job => job.UpdatedAtUtc, nowUtc)
            .Inc(job => job.Attempts, 1)
            .Inc(job => job.Version, 1);

        var result = await _jobs.UpdateOneAsync(
            session,
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }
    public async Task<BackgroundJob?> UpdatePurgeProgressAsync(
    IClientSessionHandle session,
    string jobId,
    string leaseToken,
    long expectedVersion,
    ObjectId lastProcessedReviewId,
    long deletedReviewCount,
    DateTime nowUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);

        if (!ObjectId.TryParse(jobId, out _))throw new ArgumentException("Invalid job ID.", nameof(jobId));

        if (deletedReviewCount <= 0)throw new ArgumentOutOfRangeException(nameof(deletedReviewCount));

        var filter = Builders<BackgroundJob>.Filter.And(
            Builders<BackgroundJob>.Filter.Eq(job => job.Id, jobId),
            Builders<BackgroundJob>.Filter.Eq(job => job.Type, BackgroundJobTypes.MoviePurge),
            Builders<BackgroundJob>.Filter.Eq(job => job.Status, BackgroundJobStatuses.Running),
            Builders<BackgroundJob>.Filter.Eq(job => job.LeaseToken, leaseToken),
            Builders<BackgroundJob>.Filter.Gt(job => job.LeaseExpiresAtUtc, nowUtc),
            Builders<BackgroundJob>.Filter.Eq(job => job.Version, expectedVersion)
        );

        var job = await _jobs.Find(session, filter).FirstOrDefaultAsync(cancellationToken);

        if (job is null)
            return null;

        var progress = job.Progress.DeepClone().AsBsonDocument;

        var previousDeletedCount = progress.Contains("deletedReviewCount")
            ? progress["deletedReviewCount"].ToInt64()
            : 0L;

        var previousBatchCount = progress.Contains("batchCount")
            ? progress["batchCount"].ToInt64()
            : 0L;

        progress["deletedReviewCount"] = previousDeletedCount + deletedReviewCount;

        progress["batchCount"] = previousBatchCount + 1;

        var checkpoint = new BsonDocument
        {
            { "lastProcessedReviewId", lastProcessedReviewId }
        };

        var update = Builders<BackgroundJob>.Update
            .Set(job => job.Checkpoint, checkpoint)
            .Set(job => job.Progress, progress)
            .Set(job => job.UpdatedAtUtc, nowUtc)
            .Inc(job => job.Version, 1);

        return await _jobs.FindOneAndUpdateAsync(
            session,
            filter,
            update,
            new FindOneAndUpdateOptions<BackgroundJob>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);
    }

    public async Task<bool> CompleteMoviePurgeAsync(
    IClientSessionHandle session,
    string jobId,
    string leaseToken,
    long expectedVersion,
    DateTime completedAtUtc,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseToken);

        if (!ObjectId.TryParse(jobId, out _))
            return false;

        if (expectedVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion));

        if (completedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The timestamp must be UTC.", nameof(completedAtUtc));

        var filter = Builders<BackgroundJob>.Filter.And(
            Builders<BackgroundJob>.Filter.Eq(job => job.Id, jobId),
            Builders<BackgroundJob>.Filter.Eq(job => job.Type, BackgroundJobTypes.MoviePurge),
            Builders<BackgroundJob>.Filter.Eq(job => job.Status, BackgroundJobStatuses.Running),
            Builders<BackgroundJob>.Filter.Eq(job => job.LeaseToken, leaseToken),
            Builders<BackgroundJob>.Filter.Gt(job => job.LeaseExpiresAtUtc, completedAtUtc),
            Builders<BackgroundJob>.Filter.Eq(job => job.Version, expectedVersion));

        var update = Builders<BackgroundJob>.Update
            .Set(job => job.Status, BackgroundJobStatuses.Completed)
            .Set(job => job.CompletedAtUtc, completedAtUtc)
            .Set(job => job.UpdatedAtUtc, completedAtUtc)
            .Set(job => job.LeaseToken, null)
            .Set(job => job.LeaseExpiresAtUtc, null)
            .Set(job => job.NextAttemptAtUtc, null)
            .Inc(job => job.Version, 1);

        var result = await _jobs.UpdateOneAsync(
            session,
            filter,
            update,
            cancellationToken: cancellationToken);

        return result.ModifiedCount == 1;
    }
}
