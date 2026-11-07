
using System.ComponentModel.DataAnnotations;

namespace MovieRating.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required]
    [StringLength(80, MinimumLength = 2)]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}
