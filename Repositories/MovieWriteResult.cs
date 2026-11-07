
namespace MovieRating.Repositories;

public enum MovieWriteResult
{
    Success,
    NotFoundOrInactive,
    VersionConflict,
    NotRestorable
}
