using BookOrganizer.Models;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Service for generating Audiobookshelf-compatible folder paths for audiobooks.
/// </summary>
public interface IPathGenerator
{
    /// <summary>
    /// Generates a target directory path based on audiobook metadata.
    /// Format: {Author}/{Series or Title}/
    /// Diacritics are removed by default for ASCII-safe paths.
    /// </summary>
    /// <param name="metadata">Book metadata.</param>
    /// <param name="destinationRoot">Root destination directory.</param>
    /// <returns>Full target directory path.</returns>
    string GenerateTargetPath(BookMetadata metadata, string destinationRoot);

    /// <summary>
    /// Generates a target directory path based on audiobook metadata with organization options.
    /// </summary>
    /// <param name="metadata">Book metadata.</param>
    /// <param name="destinationRoot">Root destination directory.</param>
    /// <param name="options">Organization options controlling path generation behavior.</param>
    /// <returns>Full target directory path.</returns>
    string GenerateTargetPath(BookMetadata metadata, string destinationRoot, OrganizationOptions options);

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

    /// <summary>
    /// Normalizes author name for consistent folder naming.
    /// - Fixes encoding issues (Czech diacritics)
    /// - Converts "Last, First" to "First Last"
    /// - Normalizes capitalization (title case)
    /// - Handles multiple authors (uses first author)
    /// </summary>
    /// <param name="author">Raw author name.</param>
    /// <returns>Normalized author name.</returns>
    string NormalizeAuthorName(string author);
}
