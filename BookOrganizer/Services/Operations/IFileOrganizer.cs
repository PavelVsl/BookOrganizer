using BookOrganizer.Models;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Service for executing audiobook organization operations.
/// </summary>
public interface IFileOrganizer
{
    /// <summary>
    /// Organizes audiobooks from source to destination using the specified operation type.
    /// </summary>
    /// <param name="sourcePath">Source directory containing audiobooks.</param>
    /// <param name="destinationPath">Destination directory for organized library.</param>
    /// <param name="operationType">Type of file operation to perform.</param>
    /// <param name="validateIntegrity">Whether to validate file integrity with checksums.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the organization operation.</returns>
    Task<OrganizationResult> OrganizeAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        bool validateIntegrity = true,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Organizes audiobooks from a preview/plan.
    /// </summary>
    /// <param name="plans">List of organization plans to execute.</param>
    /// <param name="validateIntegrity">Whether to validate file integrity with checksums.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the organization operation.</returns>
    Task<OrganizationResult> OrganizeFromPlansAsync(
        IEnumerable<OrganizationPlan> plans,
        bool validateIntegrity = true,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an organization operation.
/// </summary>
public record OrganizationResult
{
    /// <summary>
    /// Overall success status.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Total number of audiobooks processed.
    /// </summary>
    public int TotalAudiobooks { get; init; }

    /// <summary>
    /// Number of audiobooks successfully organized.
    /// </summary>
    public int SuccessfulAudiobooks { get; init; }

    /// <summary>
    /// Number of audiobooks that failed.
    /// </summary>
    public int FailedAudiobooks { get; init; }

    /// <summary>
    /// Total number of files processed.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Total bytes processed.
    /// </summary>
    public long TotalBytesProcessed { get; init; }

    /// <summary>
    /// Duration of the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// List of individual operation results.
    /// </summary>
    public required IReadOnlyList<AudiobookOperationResult> AudiobookResults { get; init; }

    /// <summary>
    /// Error message if overall operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result for a single audiobook organization.
/// </summary>
public record AudiobookOperationResult
{
    /// <summary>
    /// Source audiobook folder.
    /// </summary>
    public required AudiobookFolder SourceFolder { get; init; }

    /// <summary>
    /// Metadata used for organization.
    /// </summary>
    public required BookMetadata Metadata { get; init; }

    /// <summary>
    /// Target path where files were organized.
    /// </summary>
    public required string TargetPath { get; init; }

    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Number of files successfully processed.
    /// </summary>
    public int FilesProcessed { get; init; }

    /// <summary>
    /// Number of files that failed.
    /// </summary>
    public int FilesFailed { get; init; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Progress information for organization operation.
/// </summary>
public record OrganizationProgress
{
    /// <summary>
    /// Current audiobook being processed.
    /// </summary>
    public string? CurrentAudiobook { get; init; }

    /// <summary>
    /// Current file being processed.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Number of audiobooks completed.
    /// </summary>
    public int AudiobooksCompleted { get; init; }

    /// <summary>
    /// Total number of audiobooks to process.
    /// </summary>
    public int TotalAudiobooks { get; init; }

    /// <summary>
    /// Number of files completed.
    /// </summary>
    public int FilesCompleted { get; init; }

    /// <summary>
    /// Total number of files to process.
    /// </summary>
    public int TotalFiles { get; init; }

    /// <summary>
    /// Current operation stage.
    /// </summary>
    public OperationStage Stage { get; init; }

    /// <summary>
    /// Percentage complete (0.0 to 1.0).
    /// </summary>
    public double PercentComplete => TotalAudiobooks > 0
        ? (double)AudiobooksCompleted / TotalAudiobooks
        : 0.0;
}
