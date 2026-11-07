namespace MovieRating.BackgroundJobs.DeferredReviewAggregation;

public enum DeferredReviewProcessingStatus
{
    Processed,
    LeaseLost,
    MovieNotEligible,
    ConcurrencyConflict
}