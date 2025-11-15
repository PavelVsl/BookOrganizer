namespace BookOrganizer.Models;

/// <summary>
/// Represents a plan for organizing an audiobook to a new location.
/// </summary>
public record OrganizationPlan
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
    /// Target directory path where the audiobook should be organized.
    /// Format: {DestinationRoot}/{Author}/{Series}/{Book}/
    /// </summary>
    public required string TargetPath { get; init; }

    /// <summary>
    /// Whether to copy or move files.
    /// </summary>
    public required FileOperationType OperationType { get; init; }

    /// <summary>
    /// Estimated total size to be copied/moved.
    /// </summary>
    public long TotalSizeBytes => SourceFolder.TotalSizeBytes;
}

/// <summary>
/// Type of file operation to perform.
/// </summary>
public enum FileOperationType
{
    /// <summary>
    /// Copy files to the new location, keeping originals.
    /// </summary>
    Copy,

    /// <summary>
    /// Move files to the new location, removing originals.
    /// </summary>
    Move
}
