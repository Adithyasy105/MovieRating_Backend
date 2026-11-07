namespace MovieRating.Aggregation;

public enum ReviewMutationStatus
{
    Success,
    MovieNotFoundOrInactive,
    ReviewNotFound,
    VersionConflict,
    DuplicateReview
}