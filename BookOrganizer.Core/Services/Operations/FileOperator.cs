using System.Diagnostics;
using BookOrganizer.Models;
using BookOrganizer.Services.Operations.FileOperators;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Main file operator service that coordinates file operations with integrity validation.
/// </summary>
public class FileOperator : IFileOperator
{
    private readonly ILogger<FileOperator> _logger;
    private readonly ChecksumCalculator _checksumCalculator;
    private readonly IEnumerable<ISpecificFileOperator> _fileOperators;

    public FileOperator(
        ILogger<FileOperator> logger,
        ChecksumCalculator checksumCalculator,
        IEnumerable<ISpecificFileOperator> fileOperators)
    {
        _logger = logger;
        _checksumCalculator = checksumCalculator;
        _fileOperators = fileOperators;
    }

    public async Task<FileOperationResult> ExecuteFileOperationAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        bool validateIntegrity = true,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Source path cannot be null or empty", nameof(sourcePath));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));
            }

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Source file not found: {sourcePath}", sourcePath);
            }

            var fileInfo = new FileInfo(sourcePath);
            var fileSizeBytes = fileInfo.Length;

            _logger.LogInformation(
                "Starting {OperationType} operation: {Source} -> {Destination} ({Size} bytes)",
                operationType,
                sourcePath,
                destinationPath,
                fileSizeBytes);

            // Find the appropriate operator for this operation type
            var fileOperator = _fileOperators.FirstOrDefault(op => op.OperationType == operationType);
            if (fileOperator == null)
            {
                throw new NotSupportedException($"File operation type {operationType} is not supported");
            }

            // Check if the operator can execute this operation
            if (!fileOperator.CanExecute(sourcePath, destinationPath))
            {
                throw new InvalidOperationException(
                    $"Cannot execute {operationType} operation from {sourcePath} to {destinationPath}. " +
                    $"{fileOperator.GetOperationDescription()}");
            }

            // Calculate source checksum if validation is requested
            string? sourceChecksum = null;
            if (validateIntegrity && ShouldValidateIntegrity(operationType))
            {
                progress?.Report(new FileOperationProgress
                {
                    BytesProcessed = 0,
                    TotalBytes = fileSizeBytes,
                    Stage = OperationStage.CalculatingSourceChecksum,
                    CurrentFile = sourcePath
                });

                sourceChecksum = await CalculateChecksumAsync(
                    sourcePath,
                    ChecksumAlgorithm.SHA256,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogDebug(
                    "Source file checksum: {Checksum}",
                    sourceChecksum);
            }

            // Execute the file operation
            await fileOperator.ExecuteAsync(
                sourcePath,
                destinationPath,
                progress,
                cancellationToken).ConfigureAwait(false);

            // Validate destination checksum if requested
            string? destinationChecksum = null;
            if (validateIntegrity && ShouldValidateIntegrity(operationType))
            {
                progress?.Report(new FileOperationProgress
                {
                    BytesProcessed = 0,
                    TotalBytes = fileSizeBytes,
                    Stage = OperationStage.CalculatingDestinationChecksum,
                    CurrentFile = destinationPath
                });

                destinationChecksum = await CalculateChecksumAsync(
                    destinationPath,
                    ChecksumAlgorithm.SHA256,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogDebug(
                    "Destination file checksum: {Checksum}",
                    destinationChecksum);

                // Verify checksums match
                if (!string.Equals(sourceChecksum, destinationChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    // Integrity check failed - clean up destination file
                    _logger.LogError(
                        "Integrity validation FAILED! Source: {SourceChecksum}, Destination: {DestChecksum}",
                        sourceChecksum,
                        destinationChecksum);

                    CleanupDestinationFile(destinationPath, operationType);

                    throw new InvalidOperationException(
                        $"File integrity validation failed. Source checksum: {sourceChecksum}, " +
                        $"Destination checksum: {destinationChecksum}");
                }

                _logger.LogInformation("File integrity validated successfully");
            }

            stopwatch.Stop();

            progress?.Report(new FileOperationProgress
            {
                BytesProcessed = fileSizeBytes,
                TotalBytes = fileSizeBytes,
                Stage = OperationStage.Completed,
                CurrentFile = destinationPath
            });

            _logger.LogInformation(
                "Completed {OperationType} operation in {Duration}ms",
                operationType,
                stopwatch.ElapsedMilliseconds);

            return FileOperationResult.CreateSuccess(
                sourcePath,
                destinationPath,
                operationType,
                fileSizeBytes,
                stopwatch.Elapsed,
                sourceChecksum,
                destinationChecksum);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Failed to execute {OperationType} operation: {Source} -> {Destination}",
                operationType,
                sourcePath,
                destinationPath);

            return FileOperationResult.CreateFailure(
                sourcePath,
                destinationPath,
                operationType,
                ex.Message,
                stopwatch.Elapsed);
        }
    }

    public Task<string> CalculateChecksumAsync(
        string filePath,
        ChecksumAlgorithm algorithm = ChecksumAlgorithm.SHA256,
        CancellationToken cancellationToken = default)
    {
        return _checksumCalculator.CalculateChecksumAsync(
            filePath,
            algorithm,
            null,
            cancellationToken);
    }

    public Task<bool> ValidateIntegrityAsync(
        string sourcePath,
        string destinationPath,
        ChecksumAlgorithm algorithm = ChecksumAlgorithm.SHA256,
        CancellationToken cancellationToken = default)
    {
        return _checksumCalculator.ValidateIntegrityAsync(
            sourcePath,
            destinationPath,
            algorithm,
            cancellationToken);
    }

    /// <summary>
    /// Determines if integrity validation should be performed for the given operation type.
    /// Links don't need validation as they don't create new file data.
    /// </summary>
    private static bool ShouldValidateIntegrity(FileOperationType operationType)
    {
        return operationType switch
        {
            FileOperationType.Copy => true,
            FileOperationType.Move => true,
            FileOperationType.HardLink => false,  // Hard links point to same inode, no validation needed
            FileOperationType.SymbolicLink => false,  // Symlinks are just pointers, no validation needed
            _ => true
        };
    }

    /// <summary>
    /// Cleans up destination file if operation fails.
    /// For move operations, we keep the destination as the source might be gone.
    /// </summary>
    private void CleanupDestinationFile(string destinationPath, FileOperationType operationType)
    {
        try
        {
            // Don't clean up for move operations - source might already be deleted
            if (operationType == FileOperationType.Move)
            {
                _logger.LogWarning(
                    "Not cleaning up destination file for failed move operation: {Path}",
                    destinationPath);
                return;
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
                _logger.LogInformation(
                    "Cleaned up destination file after failed operation: {Path}",
                    destinationPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to clean up destination file: {Path}",
                destinationPath);
        }
    }
}
