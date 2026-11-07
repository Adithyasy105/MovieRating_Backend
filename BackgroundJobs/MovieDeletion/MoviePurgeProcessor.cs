using MongoDB.Bson;
using MongoDB.Driver;
using MovieRating.Models;
using MovieRating.Repositories;

namespace MovieRating.BackgroundJobs.MoviePurge;

public sealed class MoviePurgeProcessor
{
    private readonly IMongoClient _mongoClient;
    private readonly IBackgroundJobRepository _jobRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly ILogger<MoviePurgeProcessor> _logger;

    private readonly IReviewRepository _reviewRepository;

    public MoviePurgeProcessor(
        IMongoClient mongoClient,
        IBackgroundJobRepository jobRepository,
        IMovieRepository movieRepository,
        ILogger<MoviePurgeProcessor> logger,
        IReviewRepository reviewRepository)
    {
        _mongoClient = mongoClient?? throw new ArgumentNullException(nameof(mongoClient));

        _jobRepository = jobRepository?? throw new ArgumentNullException(nameof(jobRepository));

        _movieRepository = movieRepository?? throw new ArgumentNullException(nameof(movieRepository));

        _reviewRepository = reviewRepository ?? throw new ArgumentNullException(nameof(reviewRepository));

        _logger = logger?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<BackgroundJob?> TryClaimNextJobAsync(TimeSpan leaseDuration,CancellationToken cancellationToken = default)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        // 1. Find a possible job outside the transaction.
        var candidate = await _jobRepository.FindNextRunnableMoviePurgeAsync(DateTime.UtcNow,cancellationToken);

        if (candidate?.Id is null)
        {
            return null;
        }

        var jobId = candidate.Id;

        // Generate once so transaction retries use the same token.
        var leaseToken = Guid.NewGuid().ToString("N");

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        var transactionOptions = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            readPreference: ReadPreference.Primary,
            writeConcern: WriteConcern.WMajority);

        try
        {
            return await session.WithTransactionAsync(
                async (transactionSession, transactionCancellationToken) =>
                {
                    var nowUtc = DateTime.UtcNow;
                    var leaseExpiresAtUtc = nowUtc.Add(leaseDuration);

                    // 2. Reload the job inside the transaction.
                    var job = await _jobRepository.GetByIdAsync(transactionSession,jobId,transactionCancellationToken);

                    if (job is null ||job.Type != BackgroundJobTypes.MoviePurge)
                    {
                        return null;
                    }

                    // 3. Determine whether this is a new claim or recovery.
                    var isPending =
                        job.Status == BackgroundJobStatuses.Pending && job.NextAttemptAtUtc.HasValue && job.NextAttemptAtUtc.Value <= nowUtc;

                    var isExpiredRunning =
                        job.Status == BackgroundJobStatuses.Running && job.LeaseExpiresAtUtc.HasValue && job.LeaseExpiresAtUtc.Value <= nowUtc;

                    if (!isPending && !isExpiredRunning) //If the job is neither ready for its first execution nor eligible for recovery, don't claim it.
                    {
                        return null;
                    }

                    // 4. Extract the movie ID from the job payload.
                    var movieId = GetMovieIdFromPayload(job.Payload);

                    // 5. Read the movie inside the same transaction.
                    var movie = await _movieRepository.GetByIdAsync(transactionSession,movieId,transactionCancellationToken);

                    if (movie is null)
                    {
                        _logger.LogWarning("Movie {MovieId} for purge job {JobId} was not found.",movieId,jobId);

                        return null;
                    }

                    // 6. First claim: move the movie into Purging.
                    if (isPending)
                    {
                        if (movie.Status != "PendingDeletion" ||!movie.DeleteAfterUtc.HasValue ||movie.DeleteAfterUtc.Value > nowUtc)
                        {
                            return null;
                        }

                        var movieStarted = await _movieRepository.TryStartPurgeAsync(
                            transactionSession,
                            movieId,
                            movie.Version,
                            nowUtc,
                            transactionCancellationToken);

                        if (!movieStarted)
                        {
                            throw new ClaimConflictException();
                        }
                    }
                    // 7. Recovery: movie should already be Purging.
                    else if (movie.Status != "Purging")
                    {
                        return null;
                    }

                    // 8. Claim or reclaim the job.
                    var claimed = await _jobRepository.TryClaimAsync(
                        transactionSession,
                        jobId,
                        job.Status,
                        job.Version,
                        leaseToken,
                        nowUtc,
                        leaseExpiresAtUtc,
                        transactionCancellationToken);

                    if (!claimed)
                    {
                        // Important: abort the movie transition too.
                        throw new ClaimConflictException();
                    }

                    // Reflect the committed values in the returned object.
                    job.Status = BackgroundJobStatuses.Running;
                    job.LeaseToken = leaseToken;
                    job.LeaseExpiresAtUtc = leaseExpiresAtUtc;
                    job.NextAttemptAtUtc = null;
                    job.Attempts++;
                    job.Version++;
                    job.UpdatedAtUtc = nowUtc;

                    return job;
                },
                transactionOptions,
                cancellationToken);
        }
        catch (ClaimConflictException)
        {
            _logger.LogDebug("Another worker changed job {JobId} or its movie during claiming.",jobId);

            return null;
        }
    }

    public async Task<bool> ProcessBatchAsync(
    BackgroundJob claimedJob,
    int batchSize,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimedJob);

        if (string.IsNullOrWhiteSpace(claimedJob.Id))
            throw new ArgumentException("Job ID is required.", nameof(claimedJob));

        if (string.IsNullOrWhiteSpace(claimedJob.LeaseToken))
            throw new ArgumentException("Job lease token is required.", nameof(claimedJob));

        using var session = await _mongoClient.StartSessionAsync(
            cancellationToken: cancellationToken);

        var transactionOptions = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            readPreference: ReadPreference.Primary,
            writeConcern: WriteConcern.WMajority);

        try
        {
            return await session.WithTransactionAsync(
                async (currentSession, ct) =>
                {
                    var nowUtc = DateTime.UtcNow;

                    // 1. Reload the job inside the transaction.
                    var job = await _jobRepository.GetByIdAsync( currentSession,claimedJob.Id,ct);

                    // 2. Verify that this worker still owns a valid lease.
                    if (job is null ||
                        job.Status != BackgroundJobStatuses.Running ||
                        job.LeaseToken != claimedJob.LeaseToken ||
                        job.LeaseExpiresAtUtc is null ||
                        job.LeaseExpiresAtUtc <= nowUtc)
                    {
                        return false;
                    }

                    var movieId = GetMovieIdFromPayload(job.Payload);

                    // 3. Read the last processed review ID from the checkpoint.
                    ObjectId? lastProcessedReviewId = null;

                    if (job.Checkpoint is not null && job.Checkpoint.TryGetValue("lastProcessedReviewId", out var checkpointValue))
                    {
                        if (checkpointValue.BsonType != BsonType.ObjectId)
                            throw new InvalidOperationException("Invalid purge checkpoint.");

                        lastProcessedReviewId = checkpointValue.AsObjectId;
                    }

                    // 4. Retrieve the next bounded batch.
                    var reviews = await _reviewRepository.GetNextPurgeBatchAsync(
                        currentSession,
                        movieId,
                        lastProcessedReviewId,
                        batchSize,
                        ct);

                    // No more reviews after the current checkpoint.
                    if (reviews.Count == 0)
                        return false;

                    var reviewIds = reviews.Select(review => review.Id).ToList();

                    var validReviewIds = reviewIds.Select(id => id!).ToList();

                    if (reviewIds.Any(string.IsNullOrWhiteSpace))
                        throw new InvalidOperationException("A review in the purge batch has an invalid ID.");

                    if (!ObjectId.TryParse(reviewIds[^1], out var lastReviewObjectId))
                        throw new InvalidOperationException("The last review ID in the purge batch is invalid.");

                    // 5. Delete the selected reviews.
                    var deletedCount = await _reviewRepository.DeletePurgeBatchAsync(currentSession,movieId, validReviewIds, ct);

                    // Do not advance the checkpoint if every selected review
                    // was not deleted.
                    if (deletedCount != reviews.Count)
                        throw new InvalidOperationException("Purge batch deletion count did not match the selected batch.");

                    // 6. Save checkpoint and progress in the same transaction.
                    var updatedJob =
                        await _jobRepository.UpdatePurgeProgressAsync(
                            currentSession,
                            job.Id!,
                            job.LeaseToken!,
                            job.Version,
                            lastReviewObjectId,
                            deletedCount,
                            nowUtc,
                            ct);

                    if (updatedJob is null)
                        throw new InvalidOperationException("Could not update purge progress. The job lease or version may have changed.");

                    return true;
                },
                transactionOptions,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"Failed to process purge batch for job {JobId}.",claimedJob.Id);
            throw;
        }
    }
    public async Task<bool> FinalizePurgeAsync(
    BackgroundJob claimedJob,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claimedJob);

        if (string.IsNullOrWhiteSpace(claimedJob.Id))
            throw new ArgumentException("Job ID is required.", nameof(claimedJob));

        if (string.IsNullOrWhiteSpace(claimedJob.LeaseToken))
            throw new ArgumentException("Job lease token is required.", nameof(claimedJob));

        using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken);

        var transactionOptions = new TransactionOptions(
            readConcern: ReadConcern.Snapshot,
            readPreference: ReadPreference.Primary,
            writeConcern: WriteConcern.WMajority);

        try
        {
            return await session.WithTransactionAsync(
                async (currentSession, ct) =>
                {
                    var nowUtc = DateTime.UtcNow;

                    // 1. Reload the job and verify worker ownership.
                    var job = await _jobRepository.GetByIdAsync(currentSession,claimedJob.Id,ct);

                    if (job is null ||
                        job.Type != BackgroundJobTypes.MoviePurge ||
                        job.Status != BackgroundJobStatuses.Running ||
                        job.LeaseToken != claimedJob.LeaseToken ||
                        job.LeaseExpiresAtUtc is null ||
                        job.LeaseExpiresAtUtc <= nowUtc)
                    {
                        return false;
                    }

                    // 2. Get the movie associated with this job.
                    var movieId = GetMovieIdFromPayload(job.Payload);

                    var movie = await _movieRepository.GetByIdAsync(
                        currentSession,
                        movieId,
                        ct);

                    if (movie is null || movie.Status != "Purging")
                        return false;

                    // 3. Verify that absolutely no reviews remain.
                    var remainingReviews = await _reviewRepository.CountAllByMovieAsync(currentSession,movieId,ct);

                    if (remainingReviews > 0)
                    {
                        _logger.LogWarning("Cannot finalize purge for movie {MovieId}. {ReviewCount} reviews remain.",movieId,remainingReviews);

                        return false;
                    }

                    // 4. Mark the movie InActive and reset its rating aggregates.
                    var movieCompleted = await _movieRepository.CompletePurgeAsync(
                        currentSession,
                        movieId,
                        movie.Version,
                        nowUtc,
                        ct);

                    if (!movieCompleted)
                        throw new PurgeFinalizationConflictException();

                    // 5. Complete the job and release its lease.
                    var jobCompleted = await _jobRepository.CompleteMoviePurgeAsync(
                        currentSession,
                        job.Id!,
                        job.LeaseToken!,
                        job.Version,
                        nowUtc,
                        ct);

                    if (!jobCompleted)
                        throw new PurgeFinalizationConflictException();

                    return true;
                },
                transactionOptions,
                cancellationToken);
}
        catch (PurgeFinalizationConflictException)
        {
            _logger.LogDebug("Purge finalization conflict for job {JobId}.",claimedJob.Id);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"Failed to finalize purge for job {JobId}.",claimedJob.Id);
        
            throw;
        }
    }

    private static string GetMovieIdFromPayload(BsonDocument payload)
    {
        if (!payload.TryGetValue("movieId", out var value))
        {
            throw new InvalidOperationException("MoviePurge job payload does not contain movieId.");
        }

        if (value.IsObjectId)
        {
            return value.AsObjectId.ToString();
        }

        if (value.IsString && ObjectId.TryParse(value.AsString, out _))
        {
            return value.AsString;
        }

        throw new InvalidOperationException("MoviePurge job payload contains an invalid movieId.");
    }

    private sealed class ClaimConflictException : Exception
    {
    }
    private sealed class PurgeFinalizationConflictException : Exception
    {
    }
}