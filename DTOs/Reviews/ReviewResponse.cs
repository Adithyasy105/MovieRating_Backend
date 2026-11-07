namespace MovieRating.DTOs.Reviews;

public sealed class ReviewResponse
{
    public string Id { get; set; } = string.Empty;
    public string MovieId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string ReviewText { get; set; } = string.Empty;
    public long Version { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}