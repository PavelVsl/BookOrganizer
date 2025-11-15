using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Normalizes audio file names by removing multi-disk prefixes and other patterns
/// that cause incorrect playback order.
/// </summary>
public class FilenameNormalizer : IFilenameNormalizer
{
    private readonly ILogger<FilenameNormalizer> _logger;

    // Pattern: "01 _001. Chapter.mp3" -> "001. Chapter.mp3"
    // Matches: {track} _{chapter} where track is 01-99 and chapter is any digits
    private static readonly Regex MultiDiskPattern = new(
        @"^(\d{1,2})\s*_(\d+\.?\s*.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern: "CD1_01 Chapter.mp3" or "Disk2-05 Chapter.mp3" -> "01 Chapter.mp3"
    // Matches: CD/Disk prefix with track number
    private static readonly Regex CdDiskPattern = new(
        @"^(?:CD|Disk|Disc)\s*\d+\s*[-_]\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern: "[01] Chapter.mp3" -> "01 Chapter.mp3"
    // Removes brackets around track numbers
    private static readonly Regex BracketPattern = new(
        @"^\[(\d+)\]\s*(.+)$",
        RegexOptions.Compiled);

    public FilenameNormalizer(ILogger<FilenameNormalizer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string NormalizeFilename(string filename)
    {
        var originalFilename = filename;
        var extension = Path.GetExtension(filename);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);

        // Apply normalization patterns in order of priority
        nameWithoutExtension = ApplyMultiDiskNormalization(nameWithoutExtension);
        nameWithoutExtension = ApplyCdDiskNormalization(nameWithoutExtension);
        nameWithoutExtension = ApplyBracketNormalization(nameWithoutExtension);

        var normalizedFilename = nameWithoutExtension + extension;

        if (normalizedFilename != originalFilename)
        {
            _logger.LogDebug(
                "Normalized filename: '{Original}' -> '{Normalized}'",
                originalFilename,
                normalizedFilename);
        }

        return normalizedFilename;
    }

    /// <inheritdoc />
    public bool ShouldNormalize(IEnumerable<string> filenames)
    {
        // Check if any files match normalization patterns
        foreach (var filename in filenames)
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(filename);

            if (MultiDiskPattern.IsMatch(nameWithoutExtension) ||
                CdDiskPattern.IsMatch(nameWithoutExtension) ||
                BracketPattern.IsMatch(nameWithoutExtension))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Applies multi-disk pattern normalization.
    /// Example: "01 _001. Chapter.mp3" -> "001. Chapter.mp3"
    /// </summary>
    private string ApplyMultiDiskNormalization(string filename)
    {
        var match = MultiDiskPattern.Match(filename);
        if (match.Success)
        {
            // Return just the chapter part (group 2)
            return match.Groups[2].Value;
        }

        return filename;
    }

    /// <summary>
    /// Applies CD/Disk prefix normalization.
    /// Example: "CD1_01 Chapter.mp3" -> "01 Chapter.mp3"
    /// </summary>
    private string ApplyCdDiskNormalization(string filename)
    {
        var match = CdDiskPattern.Match(filename);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return filename;
    }

    /// <summary>
    /// Applies bracket pattern normalization.
    /// Example: "[01] Chapter.mp3" -> "01 Chapter.mp3"
    /// </summary>
    private string ApplyBracketNormalization(string filename)
    {
        var match = BracketPattern.Match(filename);
        if (match.Success)
        {
            return match.Groups[1].Value + " " + match.Groups[2].Value;
        }

        return filename;
    }
}

/// <summary>
/// Interface for filename normalization service.
/// </summary>
public interface IFilenameNormalizer
{
    /// <summary>
    /// Normalizes a filename by removing multi-disk prefixes and other patterns.
    /// </summary>
    /// <param name="filename">Original filename (with or without path).</param>
    /// <returns>Normalized filename.</returns>
    string NormalizeFilename(string filename);

    /// <summary>
    /// Checks if any files in the collection should be normalized.
    /// </summary>
    /// <param name="filenames">Collection of filenames to check.</param>
    /// <returns>True if normalization is recommended.</returns>
    bool ShouldNormalize(IEnumerable<string> filenames);
}
