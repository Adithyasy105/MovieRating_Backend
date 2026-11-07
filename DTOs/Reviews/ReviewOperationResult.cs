namespace MovieRating.DTOs.Reviews;

public enum ReviewOperationStatus
{
    Success,
    MovieNotFoundOrInactive,
    ReviewNotFound,
    VersionConflict,
    DuplicateReview
}

public sealed record ReviewOperationResult<T>(
    ReviewOperationStatus Status,
    T? Data = default);