using MongoDB.Bson;
using MovieRating.DTOs.Users;
using MovieRating.Models;
using MovieRating.Repositories;

namespace MovieRating.Services;

public sealed class UserService : IUserService
{
    private const int MaximumPageNumber = 100_000;
    private const int MaximumPageSize = 100;

    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<UserListResponse> GetAllUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > MaximumPageNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(page),$"Page must be between 1 and {MaximumPageNumber}.");
        }

        if (pageSize < 1 || pageSize > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize),$"Page size must be between 1 and {MaximumPageSize}.");
        }

        var skip = checked((page - 1) * pageSize);

        var users = await _userRepository.GetAllAsync(
            skip,
            pageSize,
            cancellationToken);

        var totalCount = await _userRepository.CountAllAsync(
            cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : checked((int)(
                totalCount / pageSize +
                (totalCount % pageSize == 0 ? 0 : 1)));

        return new UserListResponse
        {
            Items = users.Select(ToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages
        };
    }

    public async Task<UserStatusUpdateResponse> UpdateUserStatusAsync(
        string userId,
        string adminId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!ObjectId.TryParse(userId, out _))
        {
            throw new ArgumentException("Invalid user ID.", nameof(userId));
        }

        if (!ObjectId.TryParse(adminId, out _))
        {
            throw new UnauthorizedAccessException("Invalid admin identity.");
        }

        if (request.Status != UserStatuses.Active && request.Status != UserStatuses.Inactive)
        {
            throw new ArgumentException("Status must be either Active or Inactive.",nameof(request.Status));
        }

        if (request.ExpectedVersion < 1)
        {
            throw new ArgumentException("ExpectedVersion must be greater than zero.",nameof(request.ExpectedVersion));
        }

        // Prevent an administrator from deactivating their own account.
        if (request.Status == UserStatuses.Inactive && string.Equals(userId, adminId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("You cannot deactivate your own account.");
        }

        var updateResult = await _userRepository.UpdateStatusAsync(
            userId,
            request.Status,
            request.ExpectedVersion,
            cancellationToken);

        if (updateResult != UserStatusUpdateResult.Updated)
        {
            return new UserStatusUpdateResponse
            {
                Status = updateResult
            };
        }

        var updatedUser = await _userRepository.GetByIdAsync(userId,cancellationToken);

        return new UserStatusUpdateResponse
        {
            Status = UserStatusUpdateResult.Updated,
            User = updatedUser is null ? null : ToResponse(updatedUser)
        };
    }

    private static UserResponse ToResponse(User user)
    {
        return new UserResponse
        {
            Id = user.Id!,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            Status = user.Status,
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            Version = user.Version
        };
    }
}