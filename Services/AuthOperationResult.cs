using MovieRating.DTOs.Auth;

namespace MovieRating.Services;

public enum AuthOperationStatus
{
    Success,
    EmailAlreadyExists,
    InvalidCredentials
}


public sealed record AuthOperationResult(
    AuthOperationStatus Status,
    AuthResponse? Response = null);
