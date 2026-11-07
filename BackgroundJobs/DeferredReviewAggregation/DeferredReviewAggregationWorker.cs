using Microsoft.Extensions.Options;
using MovieRating.Repositories;
using System.Diagnostics;

namespace MovieRating.BackgroundJobs.DeferredReviewAggregation;

public sealed class DeferredReviewAggregationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory; //It creates a new dependency injection scope whenever you need one.
    private readonly DeferredReviewAggregationOptions _options;
    private readonly ILogger<DeferredReviewAggregationWorker> _logger;

    public DeferredReviewAggregationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<DeferredReviewAggregationOptions> options,
        ILogger<DeferredReviewAggregationWorker> logger)
    {
        _scopeFactory = scopeFactory?? throw new ArgumentNullException(nameof(scopeFactory));

        _options = options?.Value?? throw new ArgumentNullException(nameof(options));

        _logger = logger?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deferred review aggregation worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,"An unexpected error occurred in the deferred review aggregation worker.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds),stoppingToken);
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Deferred review aggregation worker stopped.");
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var batchStopwatch = Stopwatch.StartNew();

        await using var scope = _scopeFactory.CreateAsyncScope();

        var reviewRepository =scope.ServiceProvider.GetRequiredService<IReviewRepository>();

        var processor =scope.ServiceProvider.GetRequiredService<DeferredReviewProcessor>();

        _logger.LogDebug("Checking MongoDB for pending reviews.");

        var claimedReviews = await reviewRepository.ClaimPendingBatchAsync(
            _options.BatchSize,
            DateTime.UtcNow,
            TimeSpan.FromSeconds(_options.LeaseDurationSeconds),
            cancellationToken);

        if (claimedReviews.Count == 0)
        {
            _logger.LogDebug("No pending reviews found.");
            return;
        }

        _logger.LogInformation("Worker claimed {ReviewCount} reviews for processing.",claimedReviews.Count);

        var processedCount = 0;
        var leaseLostCount = 0;
        var conflictCount = 0;
        var movieNotEligibleCount = 0;
        var failedCount = 0;

        foreach (var review in claimedReviews)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(review.Id) ||
                string.IsNullOrWhiteSpace(review.ProcessingLeaseToken))
            {
                failedCount++;

                _logger.LogError("A claimed review has an invalid ID or missing lease token.");

                continue;
            }

            var reviewStopwatch = Stopwatch.StartNew();

            _logger.LogDebug(
                "Starting processing for review {ReviewId}, Movie {MovieId}, Status {Status}.",
                review.Id,
                review.MovieId,
                review.Status);

            try
            {
                var result = await processor.ProcessAsync(
                    review.Id,
                    review.ProcessingLeaseToken,
                    cancellationToken);

                switch (result)
                {
                    case DeferredReviewProcessingStatus.Processed:
                        processedCount++;

                        _logger.LogInformation(
                            "Review {ReviewId} processed successfully in {ElapsedMilliseconds} ms.",
                            review.Id,
                            reviewStopwatch.ElapsedMilliseconds);
                        break;

                    case DeferredReviewProcessingStatus.LeaseLost:
                        leaseLostCount++;

                        _logger.LogWarning(
                            "Review {ReviewId} could not be processed because its lease was lost.",
                            review.Id);
                        break;

                    case DeferredReviewProcessingStatus.ConcurrencyConflict:
                        conflictCount++;

                        _logger.LogWarning(
                            "Review {ReviewId} encountered a concurrency conflict.",
                            review.Id);
                        break;

                    case DeferredReviewProcessingStatus.MovieNotEligible:
                        movieNotEligibleCount++;

                        _logger.LogWarning(
                            "Review {ReviewId} belongs to movie {MovieId}, which is not eligible for aggregation.",
                            review.Id,
                            review.MovieId);
                        break;

                    default:
                        failedCount++;

                        _logger.LogWarning(
                            "Review {ReviewId} returned an unknown processing status: {Status}.",
                            review.Id,
                            result);
                        break;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Processing of review {ReviewId} was cancelled because the application is shutting down.",
                    review.Id);

                throw;
            }
            catch (Exception exception)
            {
                failedCount++;

                _logger.LogError(
                    exception,
                    "Failed to process review {ReviewId}. Its lease will expire and make it eligible for another attempt.",
                    review.Id);
            }
        }

        batchStopwatch.Stop();

        _logger.LogInformation(
            "Batch completed. Claimed: {ClaimedCount}, Processed: {ProcessedCount}, Lease lost: {LeaseLostCount}, Conflicts: {ConflictCount}, Movie not eligible: {MovieNotEligibleCount}, Failed: {FailedCount}, Duration: {ElapsedMilliseconds} ms.",
            claimedReviews.Count,
            processedCount,
            leaseLostCount,
            conflictCount,
            movieNotEligibleCount,
            failedCount,
            batchStopwatch.ElapsedMilliseconds);
    }
}