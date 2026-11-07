
using System.ComponentModel.DataAnnotations;

namespace MovieRating.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required(ErrorMessage = "JWT issuer is required.")]
    public string Issuer { get; set; } = string.Empty;

    [Required(ErrorMessage = "JWT audience is required.")]
    public string Audience { get; set; } = string.Empty;

    [Required(ErrorMessage = "JWT signing key is required.")]
    [MinLength(32, ErrorMessage = "JWT signing key must contain at least 32 characters.")]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440, ErrorMessage = "JWT expiration must be between 1 and 1440 minutes.")]
    public int AccessTokenExpirationMinutes { get; set; } = 30;
}
