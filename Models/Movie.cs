using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MovieRating.Models;

public class Movie
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string NameNormalized { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public int ReleaseYear { get; set; }

    public List<string> Cast { get; set; } = new();

    public long RatingSum { get; set; }

    public long RatingCount { get; set; }

    public double? AverageRating { get; set; }

    public string Status { get; set; } = "Active";

    public DateTime? DeletionRequestedAtUtc { get; set; }

    public DateTime? DeleteAfterUtc { get; set; }

    [BsonRepresentation(BsonType.ObjectId)]
    public string? DeletionRequestedBy { get; set; }

    public long Version { get; set; } = 1;

    public long ReconciledVersion { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
