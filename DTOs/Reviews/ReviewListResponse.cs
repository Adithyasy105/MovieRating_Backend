namespace MovieRating.DTOs.Reviews;

public sealed class ReviewListResponse
{
    public IReadOnlyList<ReviewResponse> Items { get; init; }= Array.Empty<ReviewResponse>();

    public int PageSize { get; init; }

    public bool HasMore { get; init; }

    public string? NextCursor { get; init; }
}


//GET /api/movies/{movieId}/reviews?pageSize=20

//{
//  "items": [],
//  "pageSize": 20,
//  "hasMore": true,
//  "nextCursor": "eyJjcmVhdGVkQXRVdGMiOi..."
//}

