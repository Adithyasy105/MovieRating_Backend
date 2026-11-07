namespace MovieRating.DTOs.Users;

public class UserListResponse
{
    public List<UserResponse> Items { get; set; } = new();

    public int Page { get; set; }

    public int PageSize { get; set; }

    public long TotalCount { get; set; }

    public int TotalPages { get; set; }
}