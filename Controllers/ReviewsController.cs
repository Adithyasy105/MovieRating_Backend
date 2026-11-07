
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MovieRating.DTOs.Reviews;
using MovieRating.Services;

namespace MovieRating.Controllers;

[ApiController]
[Route("api/movies/{movieId}/reviews")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    // GET: /api/movies/{movieId}/reviews?pageSize=20
    // GET: /api/movies/{movieId}/reviews?pageSize=20&cursor=...
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<ReviewListResponse>> GetByMovie(
        [FromRoute] string movieId,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _reviewService.GetByMovieAsync(movieId,pageSize,cursor,cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            // Includes invalid cursor values and invalid page sizes.
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new
            {
                message = "Movie not found."
            });
        }
    }

    // POST: /api/movies/{movieId}/reviews
    [HttpPost]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<ActionResult<ReviewResponse>> Create(
        [FromRoute] string movieId,
        [FromBody] CreateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();

        if (userId is null)
            return Forbid();

        var result = await _reviewService.CreateAsync(
            movieId,
            userId,
            request,
            cancellationToken);

        switch (result.Status)
        {
            case ReviewOperationStatus.Success:
                if (result.Data is null)
                    return Problem(statusCode: StatusCodes.Status500InternalServerError,title: "Review creation failed.");

                return StatusCode(StatusCodes.Status201Created,result.Data);

            case ReviewOperationStatus.MovieNotFoundOrInactive:
                return NotFound(new
                {
                    message = "Movie not found."
                });

            case ReviewOperationStatus.DuplicateReview:
                return Conflict(new
                {
                    message = "You have already reviewed this movie."
                });

            default:
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "An unexpected review operation result occurred.");
        }
    }

    // PUT: /api/movies/{movieId}/reviews/{reviewId}
    [HttpPut("{reviewId}")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<ActionResult<ReviewResponse>> Update(
        [FromRoute] string movieId,
        [FromRoute] string reviewId,
        [FromBody] UpdateReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetAuthenticatedUserId();

        if (userId is null)
            return Forbid();

        var result = await _reviewService.UpdateAsync(
            movieId,
            reviewId,
            userId,
            request,
            cancellationToken);

        switch (result.Status)
        {
            case ReviewOperationStatus.Success:
                if (result.Data is null)
                    return Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Review update failed.");

                return Ok(result.Data);

            case ReviewOperationStatus.MovieNotFoundOrInactive:
                return NotFound(new
                {
                    message = "Movie not found."
                });

            case ReviewOperationStatus.ReviewNotFound:
                return NotFound(new
                {
                    message = "Review not found."
                });

            case ReviewOperationStatus.VersionConflict:
                return Conflict(new
                {
                    message = "The review has changed. Fetch the latest version and retry."
                });

            default:
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "An unexpected review operation result occurred.");
        }
    }

    // DELETE: /api/movies/{movieId}/reviews/{reviewId}?expectedVersion=1
    [HttpDelete("{reviewId}")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<IActionResult> Delete(
        [FromRoute] string movieId,
        [FromRoute] string reviewId,
        [FromQuery] long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (expectedVersion < 1)
        {
            return BadRequest(new
            {
                message = "Expected version must be greater than zero."
            });
        }

        var userId = GetAuthenticatedUserId();

        if (userId is null)
            return Forbid();

        var result = await _reviewService.DeleteAsync(
            movieId,
            reviewId,
            userId,
            expectedVersion,
            cancellationToken);

        switch (result.Status)
        {
            case ReviewOperationStatus.Success:
                if (result.Data != true)
                    return Problem(statusCode: StatusCodes.Status500InternalServerError,title: "Review deletion failed.");
                return NoContent();

            case ReviewOperationStatus.MovieNotFoundOrInactive:
                return NotFound(new
                {
                    message = "Movie not found."
                });

            case ReviewOperationStatus.ReviewNotFound:
                return NotFound(new
                {
                    message = "Review not found."
                });

            case ReviewOperationStatus.VersionConflict:
                return Conflict(new
                {
                    message = "The review has changed. Fetch the latest version and retry."
                });

            default:
                return Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "An unexpected review operation result occurred.");
        }
    }

    private string? GetAuthenticatedUserId()
    {
        return User.FindFirstValue("sub");
    }
}
