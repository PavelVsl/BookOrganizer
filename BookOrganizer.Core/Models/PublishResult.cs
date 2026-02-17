namespace BookOrganizer.Models;

/// <summary>
/// Result of publishing a single book to the Audiobookshelf library folder.
/// </summary>
public record PublishResult(bool Success, string SourcePath, string? TargetPath, string? Error);
