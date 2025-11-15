using BookOrganizer.Models;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Service for generating Jellyfin-compatible folder paths for audiobooks.
/// </summary>
public interface IPathGenerator
{
    /// <summary>
    /// Generates a target directory path based on audiobook metadata.
    /// Format follows Jellyfin convention: Audiobooks/{Author}/{Series or Title}/
    /// </summary>
    /// <param name="metadata">Book metadata.</param>
    /// <param name="destinationRoot">Root destination directory.</param>
    /// <returns>Full target directory path.</returns>
    string GenerateTargetPath(BookMetadata metadata, string destinationRoot);

    /// <summary>
    /// Sanitizes a filename or path component for filesystem compatibility.
    /// Handles Czech characters and special characters properly.
    /// </summary>
    /// <param name="input">Input string to sanitize.</param>
    /// <returns>Sanitized string safe for filesystem use.</returns>
    string SanitizePathComponent(string input);

    /// <summary>
    /// Ensures the generated path is unique by checking existing paths and appending
    /// disambiguators if necessary (e.g., year, incrementing number).
    /// </summary>
    /// <param name="metadata">Book metadata.</param>
    /// <param name="basePath">Base path that may have collisions.</param>
    /// <param name="existingPaths">Set of paths that already exist or are planned.</param>
    /// <returns>Unique path that doesn't collide with existing paths.</returns>
    string EnsureUniquePath(BookMetadata metadata, string basePath, ISet<string> existingPaths);
}
