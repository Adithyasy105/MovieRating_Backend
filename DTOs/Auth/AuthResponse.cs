
namespace MovieRating.DTOs.Auth;

public sealed record AuthUserResponse(
    string Id,
    string DisplayName,
    string Email,
    string Role);


public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    DateTime ExpiresAtUtc,
    AuthUserResponse User);