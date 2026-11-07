using MongoDB.Driver;

namespace MovieRating.Repositories;

public interface IMovieRatingRepository
{
    Task<bool> IsActiveAsync(
        IClientSessionHandle session,
        string movieId,
        CancellationToken cancellationToken = default);


   // Deferred API: active status check without a transaction. Adding a non-session overload of
    Task<bool> IsActiveAsync(
        string movieId,
        CancellationToken cancellationToken = default);

    Task<RatingDeltaResult> ApplyRatingDeltaAsync(
        IClientSessionHandle session,
        string movieId,
        long sumDelta,
        long countDelta,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> IsEligibleForAggregationAsync(  // we need this because we are doing aggration in active and pending deletion time so
    IClientSessionHandle session,
    string movieId,
    CancellationToken cancellationToken = default);

   
}