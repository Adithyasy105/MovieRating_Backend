using MovieRating.Models;

namespace MovieRating.DTOs.Users;

public class UserStatusUpdateResponse
{
    public UserResponse? User { get; set; }

    public UserStatusUpdateResult Status { get; set; }
}