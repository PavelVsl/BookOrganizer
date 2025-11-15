using BookOrganizer.Models;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Parses metadata from folder and file names using pattern matching.
/// </summary>
public partial class FilenameParser : IFilenameParser
{
    private readonly ILogger<FilenameParser> _logger;
    private readonly ITextNormalizer _textNormalizer;

    public FilenameParser(ILogger<FilenameParser> logger, ITextNormalizer textNormalizer)
    {
        _logger = logger;
        _textNormalizer = textNormalizer;
    }

    /// <inheritdoc />
    public BookMetadata ParseFolderPath(string folderPath)
    {
        _logger.LogDebug("Parsing folder path: {Path}", folderPath);

        // Fix encoding issues in the path first
        var normalizedPath = _textNormalizer.NormalizeForDisplay(folderPath);

        // Split path into components
        var pathParts = normalizedPath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        // Try to extract metadata from path components
        string? author = null;
        string? series = null;
        string? seriesNumber = null;
        string? title = null;
        var confidence = 0.0;

        // Check last few path components for patterns
        for (int i = Math.Max(0, pathParts.Count - 3); i < pathParts.Count; i++)
        {
            var part = pathParts[i];

            // Try series pattern: "Série Legie", "Serie Michal Dabert"
            if (series == null && IsSeriesPattern(part, out var extractedSeries))
            {
                series = extractedSeries;
                confidence += 0.2;
                continue;
            }

            // Try numbered book pattern: "01 Pohled šelmy", "1. Operace Thümmel", "2 - Amanda"
            if (title == null && TryParseNumberedBook(part, out var number, out var bookTitle))
            {
                seriesNumber = number;
                title = bookTitle;
                confidence += 0.3;
                continue;
            }

            // Try author pattern (usually earlier in path)
            if (author == null && i < pathParts.Count - 1 && IsLikelyAuthorName(part))
            {
                author = NormalizeAuthorName(part, _textNormalizer);
                confidence += 0.25;
                continue;
            }

            // If no other pattern matched and we don't have a title yet, use as title
            if (title == null)
            {
                title = CleanTitle(part);
                confidence += 0.1;
            }
        }

        // If no title was found, use last path component
        if (title == null && pathParts.Count > 0)
        {
            title = CleanTitle(pathParts[^1]);
            confidence += 0.05;
        }

        return new BookMetadata
        {
            Title = title ?? "Unknown Title",
            Author = author,
            Series = series,
            SeriesNumber = seriesNumber,
            Confidence = Math.Min(confidence, 1.0),
            Source = "FilenameParser"
        };
    }

    private static bool IsSeriesPattern(string input, out string? series)
    {
        series = null;

        // Patterns: "Série Legie", "Serie Michal Dabert", "Série Tobiášův řád"
        var seriesMatch = SeriesPatternRegex().Match(input);
        if (seriesMatch.Success)
        {
            series = seriesMatch.Groups["series"].Value.Trim();
            return true;
        }

        return false;
    }

    private static bool TryParseNumberedBook(string input, out string? number, out string? title)
    {
        number = null;
        title = null;

        // Pattern 1: "01 Pohled šelmy", "1 Amanda"
        var match1 = NumberedBookPattern1().Match(input);
        if (match1.Success)
        {
            number = match1.Groups["number"].Value;
            title = match1.Groups["title"].Value.Trim();
            return true;
        }

        // Pattern 2: "1. Operace Thümmel", "01. Černá smečka"
        var match2 = NumberedBookPattern2().Match(input);
        if (match2.Success)
        {
            number = match2.Groups["number"].Value;
            title = match2.Groups["title"].Value.Trim();
            return true;
        }

        // Pattern 3: "2 - Amanda", "11 - Mirská ruleta"
        var match3 = NumberedBookPattern3().Match(input);
        if (match3.Success)
        {
            number = match3.Groups["number"].Value;
            title = match3.Groups["title"].Value.Trim();
            return true;
        }

        return false;
    }

    private static bool IsLikelyAuthorName(string input)
    {
        // Check if it looks like an author name:
        // - Contains firstname and lastname (2-3 words)
        // - Starts with uppercase
        // - Not too many special characters

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var cleaned = CleanTitle(input);
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Author names typically have 2-4 words
        if (words.Length < 2 || words.Length > 4)
            return false;

        // Check if all words start with uppercase (typical for names)
        return words.All(w => char.IsUpper(w[0]));
    }

    private static string NormalizeAuthorName(string input, ITextNormalizer textNormalizer)
    {
        // Fix encoding first, then clean
        var fixedText = textNormalizer.NormalizeForDisplay(input);
        var cleaned = CleanTitle(fixedText);

        // Handle "Lastname, Firstname" format
        if (cleaned.Contains(','))
        {
            var parts = cleaned.Split(',', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return $"{parts[1]} {parts[0]}"; // Convert to "Firstname Lastname"
            }
        }

        return cleaned;
    }

    private static string CleanTitle(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Remove common noise patterns
        var cleaned = input.Trim();

        // Remove file extensions if present
        if (Path.HasExtension(cleaned))
        {
            cleaned = Path.GetFileNameWithoutExtension(cleaned);
        }

        // Remove year patterns like "(2023)", "[2021]"
        cleaned = YearPatternRegex().Replace(cleaned, "").Trim();

        // Remove metadata patterns like "CZ audiokniha", "mluvene slovo", "Audiokniha"
        cleaned = MetadataPatternRegex().Replace(cleaned, "").Trim();

        // Normalize whitespace
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();

        return cleaned;
    }

    // Compiled regex patterns for better performance
    [GeneratedRegex(@"^(?:Série|Serie|Series)\s+(?<series>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesPatternRegex();

    [GeneratedRegex(@"^(?<number>\d{1,3})\s+(?<title>.+)$")]
    private static partial Regex NumberedBookPattern1();

    [GeneratedRegex(@"^(?<number>\d{1,3})\.\s*(?<title>.+)$")]
    private static partial Regex NumberedBookPattern2();

    [GeneratedRegex(@"^(?<number>\d{1,3})\s*-\s*(?<title>.+)$")]
    private static partial Regex NumberedBookPattern3();

    [GeneratedRegex(@"[\(\[]\d{4}[\)\]]")]
    private static partial Regex YearPatternRegex();

    [GeneratedRegex(@"\b(?:CZ\s+)?(?:audiokniha|mluvene\s+slovo|mluvené\s+slovo)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetadataPatternRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
