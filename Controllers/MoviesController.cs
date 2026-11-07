
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MovieRating.DTOs.Movies;
using MovieRating.Repositories;
using MovieRating.Services.Movies;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace MovieRating.Controllers;

[ApiController]
[Route("api/movies")]
public sealed class MoviesController : ControllerBase
{
    private readonly IMovieService _movieService;

    public MoviesController(IMovieService movieService)
    {
        ArgumentNullException.ThrowIfNull(movieService);
        _movieService = movieService;
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(MovieListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MovieListResponse>> GetMovies(
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

        var isAdmin = User.IsInRole("Admin"); //check if the user is an admin return if role is admin 

        var result = await _movieService.GetMoviesAsync(
            page,
            pageSize,
            isAdmin,
            cancellationToken);

        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{movieId}")]
    [ProducesResponseType(typeof(MovieResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieResponse>> GetMovieById(
        string movieId,
        CancellationToken cancellationToken = default)
    {
        var isAdmin = User.IsInRole("Admin");

        var movie = await _movieService.GetMovieByIdAsync(
            movieId,
            cancellationToken);

        if (movie is null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Movie not found",
                Detail = "The requested movie does not exist or is not available.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(movie);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost]
    [ProducesResponseType(typeof(MovieResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MovieResponse>> CreateMovie(
        [FromBody] CreateMovieRequest request,
        CancellationToken cancellationToken = default)
    {
        var movie = await _movieService.CreateMovieAsync(request,cancellationToken);

        return CreatedAtAction(nameof(GetMovieById),new { movieId = movie.Id },movie);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPut("{movieId}")]
    [ProducesResponseType(typeof(MovieResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MovieResponse>> UpdateMovie(
        string movieId,
        [FromBody] UpdateMovieRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _movieService.UpdateMovieAsync(movieId,request,cancellationToken);

        if (result.Status == MovieWriteResult.NotFoundOrInactive)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Movie not found",
                Detail = "The movie does not exist or is not available for updates.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (result.Status == MovieWriteResult.VersionConflict)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Movie was modified",
                Detail = "The movie has changed since you retrieved it. Fetch the latest version and retry your update.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Movie is null)
        {
            return Problem(
                title: "Movie update failed",
                detail: "The update did not return a movie representation.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(result.Movie);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("{movieId}")]
    [ProducesResponseType(typeof(MovieDeletionAcceptedResponse),StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDeletionAcceptedResponse>> RequestMovieDeletion(
        string movieId,
        [FromQuery, System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        // The subject comes from the validated JWT, not the request body.
        var requestedByUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if (string.IsNullOrWhiteSpace(requestedByUserId))
        {
            return Unauthorized();
        }

        var result = await _movieService.RequestDeletionAsync(
            movieId,
            requestedByUserId,
            expectedVersion,
            cancellationToken);

        if (result.Status == MovieWriteResult.NotFoundOrInactive)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Movie not found",
                Detail = "The movie does not exist or is not available for deletion.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (result.Status == MovieWriteResult.VersionConflict)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Movie was modified",
                Detail = "The movie has changed. Fetch the latest version before requesting deletion.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.DeleteAfterUtc is null)
        {
            return Problem(
                title: "Deletion request failed",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var response = new MovieDeletionAcceptedResponse
        {
            MovieId = movieId,
            JobId = result.JobId!,
            Status = "PendingDeletion",
            DeleteAfterUtc = result.DeleteAfterUtc.Value
        };

        return Accepted(response);
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("{movieId}/restore")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestoreMovie(
    string movieId,
    [FromQuery, System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)]
    long expectedVersion,
    CancellationToken cancellationToken = default)
    {
        var result = await _movieService.RestoreMovieAsync(movieId,expectedVersion,cancellationToken);

        if (result.Status == MovieWriteResult.NotFoundOrInactive)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Movie not found",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (result.Status == MovieWriteResult.NotRestorable)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Movie cannot be restored",
                Detail = "The movie has already entered purging or has completed deletion.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (result.Status == MovieWriteResult.VersionConflict)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Movie was modified",
                Detail = "Fetch the latest movie version and retry.",
                Status = StatusCodes.Status409Conflict
            });
        }

        return Ok(new
        {
            MovieId = movieId,
            Status = "Active",
            Message = "Movie restored successfully."
        });
    }
}
