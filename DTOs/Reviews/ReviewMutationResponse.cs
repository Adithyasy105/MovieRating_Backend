namespace MovieRating.DTOs.Reviews;

public sealed record ReviewMutationResponse(
    ReviewResponse Review,
    string Message);