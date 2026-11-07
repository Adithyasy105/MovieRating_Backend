using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MovieRating.Configuration;
using MovieRating.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MovieRating.Services;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;
    private readonly SigningCredentials _signingCredentials;

    public TokenService(IOptions<JwtOptions> jwtOptionsAccessor)
    {
        ArgumentNullException.ThrowIfNull(jwtOptionsAccessor);

        _jwtOptions = jwtOptionsAccessor.Value;

        // Convert the base64-encoded signing key to bytes.
        var keyBytes = Convert.FromBase64String(_jwtOptions.SigningKey);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("The JWT signing key must contain at least 32 bytes.");
        }

        var securityKey = new SymmetricSecurityKey(keyBytes);

        _signingCredentials = new SigningCredentials(securityKey,SecurityAlgorithms.HmacSha256);
        //{ Signature} = HMACSHA256({secret key},{ encoded header.payload})

    }

    public AccessTokenResult CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(user.Id))
        {
            throw new ArgumentException("A persisted user ID is required to create a token.",nameof(user));
        }

        if (!string.Equals(user.Status,"Active",StringComparison.Ordinal))
        {
            throw new InvalidOperationException("An active user is required to create an access token.");
        }

        if (string.IsNullOrWhiteSpace(user.Role))
        {
            throw new InvalidOperationException("A user role is required to create an access token.");
        }

        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id),

            new Claim("name", user.DisplayName),

            new Claim("role", user.Role),

            new Claim(
                JwtRegisteredClaimNames.Email,
                user.Email),

            new Claim(
                JwtRegisteredClaimNames.Jti, //JWT ID helps prevent replay attacks by providing a unique identifier for each token.
                Guid.NewGuid().ToString("N")),

            new Claim(
                JwtRegisteredClaimNames.Iat, //Issued At
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: _signingCredentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessTokenResult(tokenString,expiresAtUtc);
    }
}
