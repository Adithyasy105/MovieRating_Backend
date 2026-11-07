
namespace MovieRating.Services;

public sealed record AccessTokenResult(
    string Token,
    DateTime ExpiresAtUtc);
