namespace MovieRating.DTOs.Users;

public class UpdateUserStatusRequest
{
    public string Status { get; set; } = string.Empty;

    public long ExpectedVersion { get; set; }
}