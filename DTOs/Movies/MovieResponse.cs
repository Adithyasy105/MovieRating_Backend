
namespace MovieRating.DTOs.Movies;

public sealed class MovieResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public int ReleaseYear { get; init; }
    public IReadOnlyList<string> Cast { get; init; } = Array.Empty<string>();

    public long RatingSum { get; init; }
    public long RatingCount { get; init; }
    public double? AverageRating { get; init; }

    public long Version { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}



public sealed class MovieListResponse
{
    public IReadOnlyList<MovieResponse> Items { get; init; } = Array.Empty<MovieResponse>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public long TotalCount { get; init; }
    public int TotalPages { get; init; }
}

