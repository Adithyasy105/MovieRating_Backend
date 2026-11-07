using MovieRating.Models;

namespace MovieRating.Pagination;

public sealed record ReviewPage(
    IReadOnlyList<Review> Items,
    bool HasMore); // Represents a page of reviews with a list of review items and a flag indicating if there are more reviews available.