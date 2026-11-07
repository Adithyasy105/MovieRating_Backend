using MovieRating.Models;

namespace MovieRating.Aggregation;

public sealed record ReviewMutationResult(
    ReviewMutationStatus Status,
    Review? Review = null);
