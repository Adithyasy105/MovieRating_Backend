using System.ComponentModel.DataAnnotations;

namespace MovieRating.DTOs.Reviews;

public sealed class UpdateReviewRequest
{
    [Range(1, 10, ErrorMessage = "Rating must be between 1 and 10.")]
    public int Rating { get; init; }

    [Required(ErrorMessage = "Review text is required.")]
    [StringLength(2000,MinimumLength = 1,ErrorMessage = "Review text must contain between 1 and 2000 characters.")]
    public string ReviewText { get; init; } = string.Empty;

    [Range(1,long.MaxValue,ErrorMessage = "Version must be at least 1.")]
    public long Version { get; init; }
}