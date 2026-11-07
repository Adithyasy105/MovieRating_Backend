using System.ComponentModel.DataAnnotations;

namespace MovieRating.Configuration;

public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    [Required(ErrorMessage = "MongoDB connection string is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    [Required(ErrorMessage = "MongoDB database name is required.")]
    [MinLength(1, ErrorMessage = "MongoDB database name cannot be empty.")]
    public string DatabaseName { get; set; } = string.Empty;
}