namespace BookOrganizer.Models;

/// <summary>
/// Represents a detected audiobook folder with its audio files.
/// </summary>
public record AudiobookFolder
{
    /// <summary>
    /// Full path to the folder containing the audiobook.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// List of audio file paths within this folder.
    /// </summary>
    public required IReadOnlyList<string> AudioFiles { get; init; }

    /// <summary>
    /// Total size of all audio files in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Number of audio files detected.
    /// </summary>
    public int FileCount => AudioFiles.Count;
}
