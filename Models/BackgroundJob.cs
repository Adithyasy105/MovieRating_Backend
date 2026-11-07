
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MovieRating.Models;

public static class BackgroundJobTypes
{
    public const string MoviePurge = "MoviePurge";
    public const string TargetedReconciliation = "TargetedReconciliation";
    public const string FullReconciliation = "FullReconciliation";
}

public static class BackgroundJobStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Paused = "Paused";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public class BackgroundJob
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Status { get; set; } = BackgroundJobStatuses.Pending;

    public BsonDocument Payload { get; set; } = new();

    [BsonIgnoreIfNull]
    public BsonDocument? Checkpoint { get; set; }

    public BsonDocument Progress { get; set; } = new();

    public int Attempts { get; set; }

    [BsonIgnoreIfNull]
    public DateTime? NextAttemptAtUtc { get; set; }

    [BsonIgnoreIfNull]
    public string? LeaseToken { get; set; }

    [BsonIgnoreIfNull]
    public DateTime? LeaseExpiresAtUtc { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    [BsonIgnoreIfNull]
    public DateTime? CompletedAtUtc { get; set; }

    public long Version { get; set; } = 1;
}
