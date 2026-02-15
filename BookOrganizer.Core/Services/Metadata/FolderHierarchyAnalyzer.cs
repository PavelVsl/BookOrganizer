using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Analyzes folder hierarchy to detect author and series patterns.
/// Patterns: /Author/Series/Book, /Author/Book, etc.
/// </summary>
public partial class FolderHierarchyAnalyzer : IFolderHierarchyAnalyzer
{
    private readonly ILogger<FolderHierarchyAnalyzer> _logger;

    // Author name pattern: "Firstname Lastname" or "Lastname Firstname"
    // Czech names with diacritics supported
    [GeneratedRegex(@"^([A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+)\s+([A-ZÁČĎÉĚÍŇÓŘŠŤÚŮÝŽ][a-záčďéěíňóřšťúůýž]+)$", RegexOptions.Compiled)]
    private static partial Regex AuthorNamePattern();

    // Series indicator patterns
    [GeneratedRegex(@"^(Série|Serie|Series|Saga|Sága)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SeriesIndicatorPattern();

    public FolderHierarchyAnalyzer(ILogger<FolderHierarchyAnalyzer> logger)
    {
        _logger = logger;
    }

    public FolderHierarchyMetadata? AnalyzeHierarchy(string audiobookFolderPath, string sourceRootPath)
    {
        var normalizedAudiobookPath = Path.GetFullPath(audiobookFolderPath);
        var normalizedSourceRoot = Path.GetFullPath(sourceRootPath);

        // Ensure audiobook folder is within source root
        if (!normalizedAudiobookPath.StartsWith(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Audiobook folder {AudiobookPath} is not within source root {SourceRoot}",
                audiobookFolderPath, sourceRootPath);
            return null;
        }

        // Build path components from source root to audiobook folder
        var relativePath = Path.GetRelativePath(normalizedSourceRoot, normalizedAudiobookPath);
        var pathComponents = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p) && p != ".")
            .ToList();

        if (pathComponents.Count == 0)
        {
            return null;
        }

        string? detectedAuthor = null;
        string? detectedSeries = null;
        int? authorLevel = null;
        int? seriesLevel = null;
        double confidence = 0.0;

        // Analyze from deepest to shallowest (book -> series -> author)
        // Pattern: /Author/Series/Book or /Author/Book
        for (int i = 0; i < pathComponents.Count; i++)
        {
            var component = pathComponents[i];

            // Check if this looks like an author name (at any level above book)
            if (i < pathComponents.Count - 1 && detectedAuthor == null)
            {
                if (IsAuthorName(component))
                {
                    detectedAuthor = component;
                    authorLevel = i;
                    confidence += 0.7; // High confidence for author pattern match
                    _logger.LogDebug("Detected author '{Author}' at level {Level}", detectedAuthor, authorLevel);
                }
            }

            // Check if this looks like a series name
            // Series is typically one level above the book folder
            if (i == pathComponents.Count - 2 && detectedSeries == null)
            {
                // If has series indicator, very likely a series
                if (SeriesIndicatorPattern().IsMatch(component))
                {
                    detectedSeries = SeriesIndicatorPattern().Replace(component, "").Trim();
                    seriesLevel = i;
                    confidence += 0.8;
                    _logger.LogDebug("Detected series '{Series}' at level {Level} (with indicator)", detectedSeries, seriesLevel);
                }
                // If author detected and this is between author and book, likely a series
                else if (detectedAuthor != null && !IsAuthorName(component))
                {
                    detectedSeries = component;
                    seriesLevel = i;
                    confidence += 0.5; // Moderate confidence for positional series
                    _logger.LogDebug("Detected series '{Series}' at level {Level} (positional)", detectedSeries, seriesLevel);
                }
            }
        }

        // No author or series detected
        if (detectedAuthor == null && detectedSeries == null)
        {
            return null;
        }

        // Normalize confidence to 0.0-1.0 range
        var normalizedConfidence = Math.Min(1.0, confidence);

        return new FolderHierarchyMetadata
        {
            Author = detectedAuthor,
            Series = detectedSeries,
            Confidence = normalizedConfidence,
            AuthorLevel = authorLevel,
            SeriesLevel = seriesLevel
        };
    }

    /// <summary>
    /// Checks if a folder name looks like an author name.
    /// Pattern: "Firstname Lastname" or "Lastname Firstname"
    /// </summary>
    private static bool IsAuthorName(string folderName)
    {
        // Must match author name pattern (two capitalized words)
        if (!AuthorNamePattern().IsMatch(folderName))
        {
            return false;
        }

        // Additional heuristics: shouldn't contain numbers or common non-author patterns
        if (folderName.Any(char.IsDigit))
        {
            return false;
        }

        // Shouldn't be too long (author names are typically 2-4 words)
        var wordCount = folderName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount < 2 || wordCount > 4)
        {
            return false;
        }

        return true;
    }
}
