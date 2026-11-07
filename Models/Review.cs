using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MovieRating.Models;

public class Review
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string MovieId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    public string UserNameSnapshot { get; set; } = string.Empty;

    public int Rating { get; set; }

    public string ReviewText { get; set; } = string.Empty;

    public string Status { get; set; } = "Active";

    public long DataVersion { get; set; } = 1;

    public long ProcessedVersion { get; set; }

    public int? AppliedRating { get; set; }


    // When this review first became pending
    public DateTime? PendingSinceUtc { get; set; }

    // Worker claim information
    public string? ProcessingLeaseToken { get; set; }

    public DateTime? ProcessingLeaseExpiresAtUtc { get; set; }

    public int ProcessingAttemptCount { get; set; }


    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public long Version { get; set; } = 1;
}