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
    /// List of other file paths (cover images, metadata files, etc.) within this folder.
    /// </summary>
    public IReadOnlyList<string> OtherFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Total size of all audio files in bytes.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Number of audio files detected.
    /// </summary>
    public int FileCount => AudioFiles.Count;

    /// <summary>
    /// All files (audio + other files) in this folder.
    /// </summary>
    public IEnumerable<string> AllFiles => AudioFiles.Concat(OtherFiles);
}
