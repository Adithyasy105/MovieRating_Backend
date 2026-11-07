using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MovieRating.DTOs.Users;
using MovieRating.Models;
using MovieRating.Services;

namespace MovieRating.Controllers;

[Authorize(Policy = "AdminOnly")]
[ApiController]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserService _userService;

    public AdminUsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(UserListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserListResponse>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > 100_000)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page number",
                Detail = "Page must be between 1 and 100000.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page size",
                Detail = "Page size must be between 1 and 100.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _userService.GetAllUsersAsync(page,pageSize,cancellationToken);

        return Ok(result);
    }

    [HttpPatch("{userId}/status")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> UpdateUserStatus(
        string userId,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var adminId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(adminId))
        {
            return Unauthorized();
        }

        var result = await _userService.UpdateUserStatusAsync(
            userId,
            adminId,
            request,
            cancellationToken);

        return result.Status switch
        {
            UserStatusUpdateResult.Updated => Ok(result.User),

            UserStatusUpdateResult.NotFound => NotFound(new ProblemDetails
            {
                Title = "User not found",
                Detail = "The requested user does not exist.",
                Status = StatusCodes.Status404NotFound
            }),

            UserStatusUpdateResult.VersionConflict => Conflict(new ProblemDetails
            {
                Title = "User status conflict",
                Detail = "The user was modified by another request. Refresh and try again.",
                Status = StatusCodes.Status409Conflict
            }),

            _ => BadRequest(new ProblemDetails
            {
                Title = "Invalid user status update",
                Status = StatusCodes.Status400BadRequest
            })
        };
    }
}