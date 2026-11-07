
using System.ComponentModel.DataAnnotations;

namespace MovieRating.DTOs.Movies;

public sealed class CreateMovieRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [StringLength(5000)]
    public string Summary { get; init; } = string.Empty;

    [Range(1888, 2100)]
    public int ReleaseYear { get; init; }

    [Required]
    [MinLength(1)]
    [MaxLength(50)]
    public List<string> Cast { get; init; } = [];
}
