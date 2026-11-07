using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MovieRating.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public string Status { get; set; } = "Active";

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
   
    public long Version { get; set; } = 1;
}

public enum UserStatusUpdateResult
{
    Updated,
    NotFound,
    VersionConflict
}

public static class UserStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
}