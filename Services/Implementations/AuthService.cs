
using Microsoft.AspNetCore.Identity;
using MovieRating.DTOs.Auth;
using MovieRating.Models;
using MovieRating.Repositories;

namespace MovieRating.Services;

public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ITokenService _tokenService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher<User> passwordHasher,
        ITokenService tokenService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    public async Task<AuthOperationResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var displayName = request.DisplayName.Trim();
        var email = request.Email.Trim();
        var normalizedEmail = NormalizeEmail(email);

        // Fast duplicate check for a friendly, expected response.
        // The unique MongoDB index remains the authoritative safeguard.
        var existingUser =
            await _userRepository.GetByNormalizedEmailAsync(normalizedEmail,cancellationToken);

        if (existingUser is not null)
        {
            return new AuthOperationResult(AuthOperationStatus.EmailAlreadyExists);
        }

        var now = DateTime.UtcNow;

        var user = new User
        {
            DisplayName = displayName,
            Email = email,
            NormalizedEmail = normalizedEmail,

            Role = "User",
            Status = "Active",//magic string for user status

            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        // //H = PBKDF2(P, S, I)

        user.PasswordHash = _passwordHasher.HashPassword(user,request.Password);

        var createResult = await _userRepository.CreateAsync(user,cancellationToken);

        if (createResult == UserCreateResult.EmailAlreadyExists)
        {
            return new AuthOperationResult(AuthOperationStatus.EmailAlreadyExists);
        }

        var authResponse = CreateAuthResponse(user);

        return new AuthOperationResult(AuthOperationStatus.Success,authResponse);
    }

    public async Task<AuthOperationResult> LoginAsync(LoginRequest request,CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var normalizedEmail = NormalizeEmail(request.Email);

        var user = await _userRepository.GetByNormalizedEmailAsync(normalizedEmail,cancellationToken);

        // Use the same public error for unknown users and wrong passwords.
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
        {
            return InvalidCredentials();
        }
        // Do not issue tokens to disabled or pending-deletion accounts.
        if (!string.Equals(user.Status, "Active", StringComparison.Ordinal))
        {
            return InvalidCredentials();
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user,user.PasswordHash,request.Password);


        //failed to verify password
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }


        // If the password was verified but needs to be rehashed, do so now.
        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var upgradedHash = _passwordHasher.HashPassword(user,request.Password);

            // Update the user's password hash in the database.
            var updated = await _userRepository.UpdatePasswordHashAsync(
                user.Id!,
                upgradedHash,
                cancellationToken);

            // If the account disappeared during the upgrade, don't issue
            // a token for a user record that no longer exists.
            if (!updated)
            {
                return InvalidCredentials();
            }
        }

        // Successful login; issue a token.
        return new AuthOperationResult(AuthOperationStatus.Success,CreateAuthResponse(user));
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var tokenResult = _tokenService.CreateAccessToken(user);

        var userResponse = new AuthUserResponse(
            Id: user.Id!,
            DisplayName: user.DisplayName,
            Email: user.Email,
            Role: user.Role);

        return new AuthResponse(
            AccessToken: tokenResult.Token,
            TokenType: "Bearer",
            ExpiresAtUtc: tokenResult.ExpiresAtUtc,
            User: userResponse);
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToUpperInvariant();
    }

    private static AuthOperationResult InvalidCredentials()
    {
        return new AuthOperationResult(AuthOperationStatus.InvalidCredentials);
    }
}
