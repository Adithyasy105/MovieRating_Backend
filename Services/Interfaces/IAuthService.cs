
using MovieRating.DTOs.Auth;

namespace MovieRating.Services;

public interface IAuthService
{
    Task<AuthOperationResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    Task<AuthOperationResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);
}
