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

    // Czech diacritics mapping for normalization
    private static readonly Dictionary<char, char> CzechDiacriticsMap = new()
    {
        {'á', 'a'}, {'Á', 'a'},
        {'č', 'c'}, {'Č', 'c'},
        {'ď', 'd'}, {'Ď', 'd'},
        {'é', 'e'}, {'É', 'e'}, {'ě', 'e'}, {'Ě', 'e'},
        {'í', 'i'}, {'Í', 'i'},
        {'ň', 'n'}, {'Ň', 'n'},
        {'ó', 'o'}, {'Ó', 'o'},
        {'ř', 'r'}, {'Ř', 'r'},
        {'š', 's'}, {'Š', 's'},
        {'ť', 't'}, {'Ť', 't'},
        {'ú', 'u'}, {'Ú', 'u'}, {'ů', 'u'}, {'Ů', 'u'},
        {'ý', 'y'}, {'Ý', 'y'},
        {'ž', 'z'}, {'Ž', 'z'}
    };

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

    private static string RemoveDiacritics(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            // Check our Czech diacritics map first
            if (CzechDiacriticsMap.TryGetValue(c, out var replacement))
            {
                sb.Append(replacement);
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
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
