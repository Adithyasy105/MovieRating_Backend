namespace MovieRating.Pagination;

public sealed record ReviewCursor(
    DateTime CreatedAtUtc,
    string Id); // Represents a cursor for paginating through reviews, based on the creation timestamp and unique identifier of the review.