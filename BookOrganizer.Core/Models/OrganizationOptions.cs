namespace BookOrganizer.Models;

/// <summary>
/// Options that control how audiobooks are organized.
/// </summary>
public record OrganizationOptions
{
    /// <summary>
    /// When true, preserves Czech diacritics in folder names (UTF-8).
    /// When false, removes diacritics for ASCII-safe paths (default).
    /// </summary>
    public bool PreserveDiacritics { get; init; } = false;
}
