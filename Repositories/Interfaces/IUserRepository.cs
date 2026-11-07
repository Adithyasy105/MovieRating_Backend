using MovieRating.Models;

namespace MovieRating.Repositories;

public interface IUserRepository
{
    Task<User?> GetByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    Task<UserCreateResult> CreateAsync(
        User user,
        CancellationToken cancellationToken = default);

    Task<bool> UpdatePasswordHashAsync(
        string userId,
        string passwordHash,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<User>> GetAllAsync(
    int skip,
    int take,
    CancellationToken cancellationToken = default);

    Task<long> CountAllAsync(
        CancellationToken cancellationToken = default);

    Task<UserStatusUpdateResult> UpdateStatusAsync(
        string userId,
        string status,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}