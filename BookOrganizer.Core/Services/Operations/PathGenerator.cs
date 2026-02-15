using BookOrganizer.Models;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Generates Audiobookshelf-compatible folder paths for audiobooks.
/// </summary>
public class PathGenerator : IPathGenerator
{
    private readonly ILogger<PathGenerator> _logger;
    private readonly ITextNormalizer _textNormalizer;

    // Path component separators and formatters
    private const string SeriesBookFormat = "{0:D2} - {1}"; // "01 - Book Title"

    // Windows MAX_PATH limitation (260 characters total path length)
    // Reserve some space for drive letter (3 chars: C:\) and null terminator
    private const int MaxPathLength = 260;
    private const int WindowsReservedChars = 4; // "C:\" + null terminator
    private const int SafePathLength = MaxPathLength - WindowsReservedChars;

    // Minimum length to preserve readability
    private const int MinComponentLength = 10;

    public PathGenerator(ILogger<PathGenerator> logger, ITextNormalizer textNormalizer)
    {
        _logger = logger;
        _textNormalizer = textNormalizer;
    }

    /// <inheritdoc />
    public string GenerateTargetPath(BookMetadata metadata, string destinationRoot)
    {
        return GenerateTargetPath(metadata, destinationRoot, new OrganizationOptions());
    }

    /// <inheritdoc />
    public string GenerateTargetPath(BookMetadata metadata, string destinationRoot, OrganizationOptions options)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new ArgumentException("Destination root cannot be null or empty", nameof(destinationRoot));
        }

        if (string.IsNullOrWhiteSpace(metadata.Title))
        {
            throw new ArgumentException("Book title cannot be null or empty", nameof(metadata));
        }

        var preserveDiacritics = options.PreserveDiacritics;

        // Start with destination root
        var pathComponents = new List<string> { destinationRoot };

        // Normalize and format author name
        var rawAuthor = metadata.Author ?? "Unknown Author";
        var normalizedAuthor = NormalizeAuthorName(rawAuthor);
        var authorFolder = MaybeRemoveDiacritics(SanitizePathComponent(normalizedAuthor), preserveDiacritics);
        pathComponents.Add(authorFolder);

        // Determine if this is part of a series
        var hasSeries = !string.IsNullOrWhiteSpace(metadata.Series);

        if (hasSeries)
        {
            // Series structure: /Author/Series Name/01 - Book Title/
            var seriesFolder = MaybeRemoveDiacritics(SanitizePathComponent(metadata.Series!), preserveDiacritics);
            pathComponents.Add(seriesFolder);

            // Add book folder with series number prefix if available
            var bookFolder = GenerateBookFolderName(metadata, preserveDiacritics);
            pathComponents.Add(bookFolder);
        }
        else
        {
            // Standalone structure: /Author/Book Title/
            var bookFolder = MaybeRemoveDiacritics(SanitizePathComponent(metadata.Title), preserveDiacritics);
            pathComponents.Add(bookFolder);
        }

        var targetPath = Path.Combine(pathComponents.ToArray());

        // Check if path exceeds MAX_PATH limitation (mainly for Windows)
        if (targetPath.Length > SafePathLength)
        {
            _logger.LogWarning(
                "Generated path exceeds MAX_PATH ({Length} > {MaxLength}), attempting to truncate: {Path}",
                targetPath.Length,
                SafePathLength,
                targetPath);

            targetPath = TruncatePathToLimit(pathComponents.ToArray(), destinationRoot);
        }

        _logger.LogDebug(
            "Generated target path: {Path} (Length={Length}, Author={Author}, Series={Series}, Title={Title})",
            targetPath,
            targetPath.Length,
            metadata.Author,
            metadata.Series,
            metadata.Title);

        return targetPath;
    }

    /// <summary>
    /// Returns the input as-is when preserveDiacritics is true, otherwise removes diacritics.
    /// </summary>
    private string MaybeRemoveDiacritics(string input, bool preserveDiacritics)
    {
        return preserveDiacritics ? input : _textNormalizer.RemoveDiacritics(input);
    }

    /// <inheritdoc />
    public string SanitizePathComponent(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "Unknown";
        }

        var sanitized = input.Trim();

        // Get invalid filename characters for current OS
        var invalidChars = Path.GetInvalidFileNameChars();

        // Replace invalid characters with safe alternatives
        foreach (var invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        // Additional sanitization for common problematic characters
        sanitized = sanitized
            .Replace(":", " -")           // Colon to dash
            .Replace("?", "")             // Remove question marks
            .Replace("*", "")             // Remove asterisks
            .Replace("\"", "'")           // Double quote to single quote
            .Replace("<", "(")            // Less than to parenthesis
            .Replace(">", ")")            // Greater than to parenthesis
            .Replace("|", "-");           // Pipe to dash

        // Remove leading/trailing dots and spaces (problematic on Windows)
        sanitized = sanitized.Trim('.', ' ');

        // Collapse multiple spaces/underscores into single ones
        while (sanitized.Contains("  "))
            sanitized = sanitized.Replace("  ", " ");

        while (sanitized.Contains("__"))
            sanitized = sanitized.Replace("__", "_");

        // Ensure we have something left
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "Unknown";
        }

        return sanitized;
    }

    /// <summary>
    /// Generates the book folder name, including series number if available.
    /// </summary>
    private string GenerateBookFolderName(BookMetadata metadata, bool preserveDiacritics)
    {
        var title = metadata.Title;

        // If we have a series number, format as "01 - Book Title"
        if (!string.IsNullOrWhiteSpace(metadata.SeriesNumber))
        {
            // Try to parse series number as integer for zero-padding
            if (int.TryParse(metadata.SeriesNumber, out var seriesNum))
            {
                var bookName = string.Format(SeriesBookFormat, seriesNum, title);
                return MaybeRemoveDiacritics(SanitizePathComponent(bookName), preserveDiacritics);
            }
            else
            {
                // Use series number as-is if it's not a simple integer (e.g., "2.5", "3a")
                var bookName = $"{metadata.SeriesNumber} - {title}";
                return MaybeRemoveDiacritics(SanitizePathComponent(bookName), preserveDiacritics);
            }
        }

        // No series number, just use title
        return MaybeRemoveDiacritics(SanitizePathComponent(title), preserveDiacritics);
    }

    /// <summary>
    /// Truncates a path to stay within MAX_PATH limits while preserving important information.
    /// Priority: preserve series number > author name > series name > book title
    /// </summary>
    private string TruncatePathToLimit(string[] pathComponents, string destinationRoot)
    {
        if (pathComponents.Length < 2)
        {
            // Just root and one component - truncate that component
            var truncated = TruncateString(pathComponents[^1], SafePathLength - destinationRoot.Length - 10);
            return Path.Combine(destinationRoot, truncated);
        }

        // Calculate available space for path components (excluding root)
        var availableLength = SafePathLength - destinationRoot.Length - (pathComponents.Length - 1);

        // Distribute space intelligently among components
        var truncatedComponents = new string[pathComponents.Length];
        truncatedComponents[0] = pathComponents[0]; // Keep root as-is

        // For remaining components, prioritize from end to start
        // (book folder is most important, then series, then author)
        for (int i = 1; i < pathComponents.Length; i++)
        {
            var component = pathComponents[i];
            var maxLength = availableLength / (pathComponents.Length - i);

            // Don't truncate if not necessary
            if (component.Length <= maxLength)
            {
                truncatedComponents[i] = component;
                availableLength -= component.Length;
            }
            else
            {
                // Truncate but preserve minimum readability
                var targetLength = Math.Max(MinComponentLength, maxLength);
                truncatedComponents[i] = TruncateString(component, targetLength);
                availableLength -= truncatedComponents[i].Length;
            }
        }

        var result = Path.Combine(truncatedComponents);

        // Final check - if still too long, aggressively truncate non-root components
        if (result.Length > SafePathLength)
        {
            _logger.LogWarning(
                "Path still too long after intelligent truncation, applying aggressive truncation");

            for (int i = truncatedComponents.Length - 1; i > 0 && result.Length > SafePathLength; i--)
            {
                var excessLength = result.Length - SafePathLength;
                var currentLength = truncatedComponents[i].Length;
                var newLength = Math.Max(MinComponentLength, currentLength - excessLength);

                truncatedComponents[i] = TruncateString(pathComponents[i], newLength);
                result = Path.Combine(truncatedComponents);
            }
        }

        _logger.LogInformation(
            "Truncated path from {OriginalLength} to {NewLength} characters",
            Path.Combine(pathComponents).Length,
            result.Length);

        return result;
    }

    /// <summary>
    /// Truncates a string to the specified maximum length, preserving start and end.
    /// </summary>
    private static string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
        {
            return input;
        }

        if (maxLength < 5)
        {
            return input[..maxLength];
        }

        // Preserve beginning and end with ellipsis in middle
        // e.g., "Very Long Book Title Name" -> "Very L...Name"
        var prefixLength = (maxLength - 3) / 2;
        var suffixLength = maxLength - 3 - prefixLength;

        return $"{input[..prefixLength]}...{input[^suffixLength..]}";
    }

    /// <inheritdoc />
    public string EnsureUniquePath(BookMetadata metadata, string basePath, ISet<string> existingPaths)
    {
        if (existingPaths == null)
        {
            throw new ArgumentNullException(nameof(existingPaths));
        }

        // If path is already unique, return it
        if (!existingPaths.Contains(basePath))
        {
            return basePath;
        }

        _logger.LogDebug("Path collision detected for: {Path}", basePath);

        // Try appending year if available
        if (metadata.Year.HasValue)
        {
            var pathWithYear = $"{basePath} ({metadata.Year})";
            if (!existingPaths.Contains(pathWithYear))
            {
                _logger.LogInformation(
                    "Resolved path collision by adding year: {OriginalPath} -> {NewPath}",
                    basePath,
                    pathWithYear);
                return pathWithYear;
            }
        }

        // Fallback to incrementing number suffix
        var counter = 2;
        string uniquePath;
        do
        {
            uniquePath = $"{basePath} ({counter})";
            counter++;
        }
        while (existingPaths.Contains(uniquePath) && counter < 100); // Safety limit

        if (counter >= 100)
        {
            _logger.LogWarning(
                "Collision resolution reached safety limit (100 attempts) for path: {Path}",
                basePath);
        }
        else
        {
            _logger.LogInformation(
                "Resolved path collision by adding suffix: {OriginalPath} -> {NewPath}",
                basePath,
                uniquePath);
        }

        return uniquePath;
    }

    /// <summary>
    /// Normalizes author name for consistent folder naming.
    /// - Fixes encoding issues (Czech diacritics)
    /// - Converts "Last, First" to "First Last"
    /// - Normalizes capitalization (title case)
    /// - Handles multiple authors (uses first author)
    /// </summary>
    public string NormalizeAuthorName(string author)
    {
        if (string.IsNullOrWhiteSpace(author))
        {
            return "Unknown Author";
        }

        // Fix encoding issues and normalize for display
        var normalized = _textNormalizer.NormalizeForDisplay(author);

        // Handle multiple authors - use first one
        if (normalized.Contains(';'))
        {
            normalized = normalized.Split(';')[0].Trim();
        }

        // Convert "Last, First" to "First Last" format
        if (normalized.Contains(','))
        {
            var parts = normalized.Split(',', 2);
            if (parts.Length == 2)
            {
                var lastName = parts[0].Trim();
                var firstName = parts[1].Trim();
                normalized = $"{firstName} {lastName}";
            }
        }

        // Always normalize capitalization to title case
        normalized = NormalizeCapitalization(normalized);

        return normalized;
    }

    /// <summary>
    /// Normalizes capitalization to title case.
    /// Always converts to title case for consistency, regardless of source capitalization.
    /// </summary>
    private static string NormalizeCapitalization(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Always convert to title case for consistent folder names
        var textInfo = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(text.ToLowerInvariant());
    }
}
