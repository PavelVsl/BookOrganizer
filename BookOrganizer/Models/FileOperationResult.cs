namespace BookOrganizer.Models;

/// <summary>
/// Represents the result of a file operation.
/// </summary>
public record FileOperationResult
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Source file path.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Destination file path.
    /// </summary>
    public required string DestinationPath { get; init; }

    /// <summary>
    /// Type of operation performed.
    /// </summary>
    public required FileOperationType OperationType { get; init; }

    /// <summary>
    /// Size of the file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Checksum of the source file (if validation was performed).
    /// </summary>
    public string? SourceChecksum { get; init; }

    /// <summary>
    /// Checksum of the destination file (if validation was performed).
    /// </summary>
    public string? DestinationChecksum { get; init; }

    /// <summary>
    /// Whether integrity validation was performed and passed.
    /// </summary>
    public bool IntegrityValidated { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Duration of the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Creates a successful operation result.
    /// </summary>
    public static FileOperationResult CreateSuccess(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        long fileSizeBytes,
        TimeSpan duration,
        string? sourceChecksum = null,
        string? destinationChecksum = null)
    {
        return new FileOperationResult
        {
            Success = true,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            OperationType = operationType,
            FileSizeBytes = fileSizeBytes,
            SourceChecksum = sourceChecksum,
            DestinationChecksum = destinationChecksum,
            IntegrityValidated = sourceChecksum != null && destinationChecksum != null,
            Duration = duration
        };
    }

    /// <summary>
    /// Creates a failed operation result.
    /// </summary>
    public static FileOperationResult CreateFailure(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        string errorMessage,
        TimeSpan duration)
    {
        return new FileOperationResult
        {
            Success = false,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            OperationType = operationType,
            ErrorMessage = errorMessage,
            Duration = duration
        };
    }
}

/// <summary>
/// Represents progress information for a file operation.
/// </summary>
public record FileOperationProgress
{
    /// <summary>
    /// Number of bytes processed so far.
    /// </summary>
    public required long BytesProcessed { get; init; }

    /// <summary>
    /// Total number of bytes to process.
    /// </summary>
    public required long TotalBytes { get; init; }

    /// <summary>
    /// Current stage of the operation.
    /// </summary>
    public required OperationStage Stage { get; init; }

    /// <summary>
    /// File currently being processed.
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Percentage of completion (0.0 to 1.0).
    /// </summary>
    public double PercentComplete => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes : 0.0;
}

/// <summary>
/// Stage of a file operation.
/// </summary>
public enum OperationStage
{
    /// <summary>
    /// Preparing for the operation (creating directories, validating paths).
    /// </summary>
    Preparing,

    /// <summary>
    /// Calculating checksum of source file.
    /// </summary>
    CalculatingSourceChecksum,

    /// <summary>
    /// Copying or moving the file.
    /// </summary>
    TransferringFile,

    /// <summary>
    /// Calculating checksum of destination file.
    /// </summary>
    CalculatingDestinationChecksum,

    /// <summary>
    /// Validating file integrity.
    /// </summary>
    ValidatingIntegrity,

    /// <summary>
    /// Cleaning up after the operation.
    /// </summary>
    CleaningUp,

    /// <summary>
    /// Operation completed.
    /// </summary>
    Completed
}
