namespace BookOrganizer.Services.Text;

/// <summary>
/// Normalizes text for comparison and display, handling encoding issues and Czech diacritics.
/// </summary>
public interface ITextNormalizer
{
    /// <summary>
    /// Normalizes text for comparison purposes (removes diacritics, converts to lowercase).
    /// </summary>
    /// <param name="text">Text to normalize</param>
    /// <returns>Normalized text suitable for case-insensitive comparison</returns>
    string NormalizeForComparison(string? text);

    /// <summary>
    /// Detects and fixes encoding issues (e.g., Windows-1250 incorrectly read as UTF-8).
    /// Returns the properly decoded text.
    /// </summary>
    /// <param name="text">Text that may have encoding issues</param>
    /// <returns>Text with encoding issues fixed, or original if no issues detected</returns>
    string FixEncoding(string? text);

    /// <summary>
    /// Normalizes text for display (fixes encoding, preserves case and diacritics).
    /// </summary>
    /// <param name="text">Text to normalize</param>
    /// <returns>Display-ready text with encoding issues fixed</returns>
    string NormalizeForDisplay(string? text);

    /// <summary>
    /// Compares two text strings in a Czech-aware manner (encoding-tolerant, case-insensitive).
    /// </summary>
    /// <param name="text1">First text</param>
    /// <param name="text2">Second text</param>
    /// <returns>True if texts are equivalent after normalization</returns>
    bool AreEquivalent(string? text1, string? text2);

    /// <summary>
    /// Calculates fuzzy similarity between two strings (0.0 = completely different, 1.0 = identical).
    /// Handles encoding variations, case differences, and minor typos.
    /// </summary>
    /// <param name="text1">First text</param>
    /// <param name="text2">Second text</param>
    /// <returns>Similarity score from 0.0 to 1.0</returns>
    double CalculateSimilarity(string? text1, string? text2);

    /// <summary>
    /// Removes diacritics from text while preserving case and other characters.
    /// Uses Unicode NFD decomposition to handle all diacritical marks.
    /// </summary>
    /// <param name="text">Text with diacritics</param>
    /// <returns>Text with diacritics removed, case preserved</returns>
    string RemoveDiacritics(string? text);
}
