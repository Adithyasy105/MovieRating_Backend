using MovieRating.DTOs.Users;

namespace MovieRating.Services;

public interface IUserService
{
    Task<UserListResponse> GetAllUsersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<UserStatusUpdateResponse> UpdateUserStatusAsync(
        string userId,
        string adminId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken = default);
}