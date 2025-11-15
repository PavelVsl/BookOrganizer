namespace BookOrganizer.Models;

/// <summary>
/// Represents a complete preview of the organization operation.
/// </summary>
public record PreviewResult
{
    /// <summary>
    /// List of all file operations planned.
    /// </summary>
    public required IReadOnlyList<FileOperationPreview> Operations { get; init; }

    /// <summary>
    /// Statistics about the planned operations.
    /// </summary>
    public required PreviewStatistics Statistics { get; init; }

    /// <summary>
    /// List of potential issues or warnings found during preview generation.
    /// </summary>
    public required IReadOnlyList<PreviewIssue> Issues { get; init; }

    /// <summary>
    /// List of potential duplicate audiobooks detected.
    /// </summary>
    public IReadOnlyList<DuplicationCandidate> PotentialDuplicates { get; init; } = Array.Empty<DuplicationCandidate>();

    /// <summary>
    /// Time when the preview was generated.
    /// </summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Represents a single file operation in the preview.
/// </summary>
public record FileOperationPreview
{
    /// <summary>
    /// Source audiobook folder.
    /// </summary>
    public required AudiobookFolder SourceFolder { get; init; }

    /// <summary>
    /// Consolidated metadata for the audiobook.
    /// </summary>
    public required BookMetadata Metadata { get; init; }

    /// <summary>
    /// Source path of the folder/files.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Destination path where files will be organized.
    /// </summary>
    public required string DestinationPath { get; init; }

    /// <summary>
    /// Type of file operation that will be performed.
    /// </summary>
    public required FileOperationType OperationType { get; init; }

    /// <summary>
    /// Total size in bytes of files to be copied/moved.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Number of audio files in this audiobook.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// List of potential issues specific to this operation.
    /// </summary>
    public IReadOnlyList<PreviewIssue> Issues { get; init; } = Array.Empty<PreviewIssue>();
}

/// <summary>
/// Statistics about the entire preview operation.
/// </summary>
public record PreviewStatistics
{
    /// <summary>
    /// Total number of audiobooks to be organized.
    /// </summary>
    public int TotalAudiobooks { get; init; }

    /// <summary>
    /// Total number of files to be processed.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Total size in bytes of all files.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Estimated disk space required for copy operations.
    /// For move operations, this is 0. For links, this is minimal.
    /// </summary>
    public long EstimatedDiskSpaceBytes { get; init; }

    /// <summary>
    /// Number of operations by type.
    /// </summary>
    public required IReadOnlyDictionary<FileOperationType, int> OperationCounts { get; init; }

    /// <summary>
    /// Number of issues found (errors, warnings, info).
    /// </summary>
    public required IReadOnlyDictionary<IssueSeverity, int> IssueCounts { get; init; }

    /// <summary>
    /// Estimated time to complete the operations.
    /// Based on average transfer speeds and file sizes.
    /// </summary>
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>
    /// Gets human-readable total size string.
    /// </summary>
    public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);

    /// <summary>
    /// Gets human-readable estimated disk space string.
    /// </summary>
    public string EstimatedDiskSpaceFormatted => FormatBytes(EstimatedDiskSpaceBytes);

    /// <summary>
    /// Formats bytes into human-readable string (KB, MB, GB, etc.).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Represents a potential issue found during preview generation.
/// </summary>
public record PreviewIssue
{
    /// <summary>
    /// Severity level of the issue.
    /// </summary>
    public required IssueSeverity Severity { get; init; }

    /// <summary>
    /// Type/category of the issue.
    /// </summary>
    public required IssueType Type { get; init; }

    /// <summary>
    /// Human-readable description of the issue.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Source path related to the issue, if applicable.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Destination path related to the issue, if applicable.
    /// </summary>
    public string? DestinationPath { get; init; }

    /// <summary>
    /// Additional context or suggested fixes.
    /// </summary>
    public string? Suggestion { get; init; }
}

/// <summary>
/// Severity level of a preview issue.
/// </summary>
public enum IssueSeverity
{
    /// <summary>
    /// Informational message, not a problem.
    /// </summary>
    Info,

    /// <summary>
    /// Warning about potential problems that may not block the operation.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that will likely prevent the operation from succeeding.
    /// </summary>
    Error
}

/// <summary>
/// Type/category of a preview issue.
/// </summary>
public enum IssueType
{
    /// <summary>
    /// Missing or incomplete metadata.
    /// </summary>
    MissingMetadata,

    /// <summary>
    /// Destination file or folder already exists.
    /// </summary>
    PathCollision,

    /// <summary>
    /// Path length exceeds OS limits (260 chars on Windows, etc.).
    /// </summary>
    PathTooLong,

    /// <summary>
    /// Invalid characters in path or filename.
    /// </summary>
    InvalidCharacters,

    /// <summary>
    /// Insufficient disk space for the operation.
    /// </summary>
    InsufficientSpace,

    /// <summary>
    /// Permission or access issues.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// Operation not supported (e.g., hard link across volumes).
    /// </summary>
    UnsupportedOperation,

    /// <summary>
    /// Low confidence in metadata extraction.
    /// </summary>
    LowConfidence,

    /// <summary>
    /// Potential duplicate audiobook detected.
    /// </summary>
    PotentialDuplicate,

    /// <summary>
    /// General information message.
    /// </summary>
    Information
}
