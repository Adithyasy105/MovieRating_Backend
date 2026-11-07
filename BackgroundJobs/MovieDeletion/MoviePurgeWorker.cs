using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace MovieRating.BackgroundJobs.MoviePurge;

public sealed class MoviePurgeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MoviePurgeWorker> _logger;
    private readonly MoviePurgeOptions _options;

    public MoviePurgeWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MoviePurgeOptions> options,
        ILogger<MoviePurgeWorker> logger)
    {
        _scopeFactory = scopeFactory?? throw new ArgumentNullException(nameof(scopeFactory));

        _options = options.Value?? throw new ArgumentNullException(nameof(options));

        _logger = logger?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "== MOVIE PURGE WORKER STARTED == PollIntervalSeconds: {PollIntervalSeconds}, BatchSize: {BatchSize}, LeaseDurationSeconds: {LeaseDurationSeconds}",
            _options.PollIntervalSeconds,
            _options.BatchSize,
            _options.LeaseDurationSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobProcessed = await ProcessNextJobAsync(stoppingToken);

                if (!jobProcessed)
                {
                    _logger.LogDebug("No runnable movie purge job was found. Next poll in {PollIntervalSeconds} seconds.", _options.PollIntervalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds),stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Error occurred in Movie Purge Worker.");

                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds),stoppingToken);
            }
        }

        _logger.LogInformation("== MOVIE PURGE WORKER STOPPED ==");
    }

    private async Task<bool> ProcessNextJobAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var processor = scope.ServiceProvider.GetRequiredService<MoviePurgeProcessor>();

        // 1. Claim the next runnable job.
        var job = await processor.TryClaimNextJobAsync(TimeSpan.FromSeconds(_options.LeaseDurationSeconds),cancellationToken);

        if (job is null)
            return false;

        var jobStopwatch = Stopwatch.StartNew();

        var batchNumber = 0;

        _logger.LogInformation("== PURGE JOB STARTED | JobId: {JobId} ==",job.Id);

        // 2. Process batches until no more work is available.
        while (!cancellationToken.IsCancellationRequested)
        {
            batchNumber++;

            var batchStopwatch = Stopwatch.StartNew();

            _logger.LogInformation("[Job: {JobId}] Batch {BatchNumber} started.",job.Id,batchNumber);

            var batchProcessed = await processor.ProcessBatchAsync(job,_options.BatchSize,cancellationToken);

            batchStopwatch.Stop();

            if (!batchProcessed)
            {
                batchNumber--;

                _logger.LogInformation(
                    "[Job: {JobId}] No more batches available. Last batch processing time: {ElapsedMs} ms.",job.Id,
                    batchStopwatch.ElapsedMilliseconds);

                break;
            }

            _logger.LogInformation(
                "[Job: {JobId}] Batch {BatchNumber} completed in {ElapsedMs} ms.",
                job.Id,
                batchNumber,
                batchStopwatch.ElapsedMilliseconds);
        }

        // 3. Finalize the purge.
        var finalizationStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("[Job: {JobId}] Starting purge finalization.",job.Id);

        var completed = await processor.FinalizePurgeAsync(job,cancellationToken);

        finalizationStopwatch.Stop();

        jobStopwatch.Stop();

        if (completed)
        {
            _logger.LogInformation(
                """
                == PURGE JOB COMPLETED ==
                Job ID          : {JobId}
                Total Batches   : {BatchCount}
                Finalization    : {FinalizationMs} ms
                Total Time      : {TotalTimeMs} ms
                ==
                """,
                job.Id,
                batchNumber,
                finalizationStopwatch.ElapsedMilliseconds,
                jobStopwatch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogWarning(
                """
                == PURGE JOB NOT FINALIZED ==
                Job ID          : {JobId}
                Total Batches   : {BatchCount}
                Finalization    : {FinalizationMs} ms
                Total Time      : {TotalTimeMs} ms
                ==
                """,
                job.Id,
                batchNumber,
                finalizationStopwatch.ElapsedMilliseconds,
                jobStopwatch.ElapsedMilliseconds);
        }

        return true;
    }
}