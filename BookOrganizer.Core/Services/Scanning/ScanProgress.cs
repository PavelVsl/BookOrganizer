namespace BookOrganizer.Services.Scanning;

/// <summary>
/// Represents the progress of a directory scanning operation.
/// </summary>
public record ScanProgress
{
    /// <summary>
    /// Number of directories scanned so far.
    /// </summary>
    public int DirectoriesScanned { get; init; }

    /// <summary>
    /// Number of audiobook folders found so far.
    /// </summary>
    public int AudiobookFoldersFound { get; init; }

    /// <summary>
    /// Total number of audio files found so far.
    /// </summary>
    public int AudioFilesFound { get; init; }

    /// <summary>
    /// Current directory being scanned.
    /// </summary>
    public string? CurrentDirectory { get; init; }

    /// <summary>
    /// Whether the scan is complete.
    /// </summary>
    public bool IsComplete { get; init; }
}
