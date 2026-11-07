
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MovieRating.DTOs.Auth;
using MovieRating.Services;

namespace MovieRating.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(request,cancellationToken);

        switch (result.Status)
        {
            case AuthOperationStatus.Success:
                return StatusCode(StatusCodes.Status201Created,result.Response);

            case AuthOperationStatus.EmailAlreadyExists:
                return Conflict(new
                {
                    message = "An account with this email already exists."
                });

            default:
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Authentication operation failed.");
        }
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(request,cancellationToken);

        if (result.Status != AuthOperationStatus.Success || result.Response is null)
        {
            return Unauthorized(
            new{
                message = "Invalid email or password."
             });
        }

        return Ok(result.Response);
    }
}
