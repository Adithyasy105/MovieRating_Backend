
namespace MovieRating.DTOs.Movies;

//public sealed class MovieDeletionAcceptedResponse
//{
//    public string MovieId { get; init; } = string.Empty;
//    public string Status { get; init; } = "PendingDeletion";
//    public DateTime DeleteAfterUtc { get; init; }
//}

public sealed class MovieDeletionAcceptedResponse
{
    public string MovieId { get; init; } = string.Empty;

    public string JobId { get; init; } = string.Empty;

    public string Status { get; init; } = "PendingDeletion";

    public DateTime DeleteAfterUtc { get; init; }
}