namespace MovieRating.BackgroundJobs.DeferredReviewAggregation;

public sealed class DeferredReviewAggregationOptions
{
    public const string SectionName = "DeferredReviewAggregation";

    public int BatchSize { get; set; } = 10; //Maximum reviews claimed in one batch

    public int PollIntervalSeconds { get; set; } = 5;//How long the worker waits before checking again

    public int LeaseDurationSeconds { get; set; } = 120;//How long a claimed review is reserved for a worker
}