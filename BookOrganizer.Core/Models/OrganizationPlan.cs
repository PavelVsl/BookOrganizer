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
    /// Type of file operation to perform (Copy, Move, HardLink, or SymbolicLink).
    /// </summary>
    public required FileOperationType OperationType { get; init; }

    /// <summary>
    /// Library root path (set during reorganization for .duplicates folder placement).
    /// </summary>
    public string? LibraryPath { get; init; }

    /// <summary>
    /// Estimated total size to be copied/moved.
    /// For link operations, this represents the size of source files (no actual disk usage increase).
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
    /// Uses more disk space but provides full independence.
    /// </summary>
    Copy,

    /// <summary>
    /// Move files to the new location, removing originals.
    /// Saves disk space but changes original location.
    /// </summary>
    Move,

    /// <summary>
    /// Create hard links to files in the new location.
    /// Files remain in original location, no additional disk space used.
    /// Both paths point to the same physical file on disk.
    /// Note: Hard links only work within the same filesystem/volume.
    /// </summary>
    HardLink,

    /// <summary>
    /// Create symbolic links (symlinks) to files in the new location.
    /// Files remain in original location, minimal disk space used.
    /// Links can span different filesystems/volumes.
    /// Note: Requires appropriate permissions (admin on Windows).
    /// </summary>
    SymbolicLink
}
