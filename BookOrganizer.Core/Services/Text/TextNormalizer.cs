using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Text;

/// <summary>
/// Normalizes Czech text for comparison and display, handling encoding issues.
/// </summary>
public class TextNormalizer : ITextNormalizer
{
    private readonly ILogger<TextNormalizer> _logger;

    // Common mojibake patterns when Windows-1250 is read as UTF-8
    // These are Czech characters incorrectly decoded
    private static readonly Dictionary<string, string> MojibakePatterns = new()
    {
        // á (0xE1 in Windows-1250)
        {"á", "á"},
        // č (0xE8 in Windows-1250)
        {"č", "č"},
        // ď (0xEF in Windows-1250)
        {"ď", "ď"},
        // é (0xE9 in Windows-1250)
        {"é", "é"},
        // ě (0xEC in Windows-1250)
        {"ě", "ě"},
        // í (0xED in Windows-1250)
        {"í", "í"},
        // ň (0xF2 in Windows-1250)
        {"ň", "ň"},
        // ó (0xF3 in Windows-1250)
        {"ó", "ó"},
        // ř (0xF8 in Windows-1250)
        {"ř", "ř"},
        // š (0xB9 in Windows-1250)
        {"š", "š"},
        // ť (0x9D in Windows-1250)
        {"ť", "ť"},
        // ú (0xFA in Windows-1250)
        {"ú", "ú"},
        // ů (0xF9 in Windows-1250)
        {"ů", "ů"},
        // ý (0xFD in Windows-1250)
        {"ý", "ý"},
        // ž (0x9E in Windows-1250)
        {"ž", "ž"},
        // Uppercase variants
        {"Č", "Č"},
        {"Ř", "Ř"},
        {"Š", "Š"},
        {"Ž", "Ž"},
        {"Ě", "Ě"},
        {"Ú", "Ú"},
        {"Ů", "Ů"},
        {"Ý", "Ý"},
        {"Á", "Á"},
        {"É", "É"},
        {"Í", "Í"},
        {"Ó", "Ó"},
        {"Ď", "Ď"},
        {"Ť", "Ť"},
        {"Ň", "Ň"}
    };

    public TextNormalizer(ILogger<TextNormalizer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string NormalizeForComparison(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // First fix any encoding issues
        var fixedText = FixEncoding(text);

        // Remove diacritics
        var normalized = RemoveDiacritics(fixedText);

        // Lowercase and trim
        return normalized.ToLowerInvariant().Trim();
    }

    /// <inheritdoc />
    public string FixEncoding(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Check if text contains mojibake patterns
        if (!ContainsMojibake(text))
            return text;

        try
        {
            // Try to fix Windows-1250 → UTF-8 encoding issue
            var fixedText = FixWindows1250Mojibake(text);

            if (fixedText != text)
            {
                _logger.LogDebug(
                    "Fixed encoding issue: '{Original}' → '{Fixed}'",
                    text.Length > 50 ? text[..50] + "..." : text,
                    fixedText.Length > 50 ? fixedText[..50] + "..." : fixedText);
            }

            return fixedText;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fix encoding for text: {Text}", text);
            return text;
        }
    }

    /// <inheritdoc />
    public string NormalizeForDisplay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Fix encoding issues but preserve case and diacritics
        var fixedText = FixEncoding(text);

        // Normalize whitespace
        return NormalizeWhitespace(fixedText);
    }

    /// <inheritdoc />
    public bool AreEquivalent(string? text1, string? text2)
    {
        var normalized1 = NormalizeForComparison(text1);
        var normalized2 = NormalizeForComparison(text2);

        return string.Equals(normalized1, normalized2, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public double CalculateSimilarity(string? text1, string? text2)
    {
        if (string.IsNullOrWhiteSpace(text1) && string.IsNullOrWhiteSpace(text2))
            return 1.0;

        if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            return 0.0;

        // Normalize both strings for comparison
        var normalized1 = NormalizeForComparison(text1);
        var normalized2 = NormalizeForComparison(text2);

        // Exact match after normalization
        if (normalized1 == normalized2)
            return 1.0;

        // Calculate Levenshtein distance
        var distance = CalculateLevenshteinDistance(normalized1, normalized2);
        var maxLength = Math.Max(normalized1.Length, normalized2.Length);

        // Convert distance to similarity score (0.0 to 1.0)
        return 1.0 - ((double)distance / maxLength);
    }

    /// <summary>
    /// Calculates the Levenshtein distance (edit distance) between two strings.
    /// </summary>
    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;

        if (string.IsNullOrEmpty(target))
            return source.Length;

        var sourceLength = source.Length;
        var targetLength = target.Length;

        // Create distance matrix
        var distance = new int[sourceLength + 1, targetLength + 1];

        // Initialize first column and row
        for (var i = 0; i <= sourceLength; i++)
            distance[i, 0] = i;

        for (var j = 0; j <= targetLength; j++)
            distance[0, j] = j;

        // Calculate distances
        for (var i = 1; i <= sourceLength; i++)
        {
            for (var j = 1; j <= targetLength; j++)
            {
                var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(
                        distance[i - 1, j] + 1,      // Deletion
                        distance[i, j - 1] + 1),     // Insertion
                    distance[i - 1, j - 1] + cost);  // Substitution
            }
        }

        return distance[sourceLength, targetLength];
    }

    /// <inheritdoc />
    public string RemoveDiacritics(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Use Unicode NFD decomposition to separate base characters from combining marks
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(text.Length);

        foreach (var c in normalized)
        {
            // Keep only non-combining characters (skip combining diacritical marks)
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        // Re-compose to NFC for clean output
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static bool ContainsMojibake(string text)
    {
        // Check for common mojibake patterns
        // If we find replacement character (�) or certain suspicious byte sequences
        if (text.Contains('\uFFFD')) // Unicode replacement character
            return true;

        // Check for mojibake patterns in our dictionary
        foreach (var pattern in MojibakePatterns.Keys)
        {
            if (text.Contains(pattern))
                return true;
        }

        return false;
    }

    private static string FixWindows1250Mojibake(string text)
    {
        var result = text;

        // Replace known mojibake patterns
        foreach (var (mojibake, correct) in MojibakePatterns)
        {
            result = result.Replace(mojibake, correct);
        }

        return result;
    }

    private static string NormalizeWhitespace(string text)
    {
        // Replace multiple spaces with single space
        var normalized = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return normalized.Trim();
    }
}
