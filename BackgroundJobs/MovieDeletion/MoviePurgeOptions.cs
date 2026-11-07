namespace MovieRating.BackgroundJobs.MoviePurge;

public sealed class MoviePurgeOptions
{
    public int BatchSize { get; set; } = 100;

    public int PollIntervalSeconds { get; set; } = 5;

    public int LeaseDurationSeconds { get; set; } = 120;
}