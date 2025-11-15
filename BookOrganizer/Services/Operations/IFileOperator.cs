using BookOrganizer.Models;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Service for performing file operations with integrity validation and progress tracking.
/// </summary>
public interface IFileOperator
{
    /// <summary>
    /// Copies or moves a single file with integrity validation.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Destination file path.</param>
    /// <param name="operationType">Type of operation (Copy or Move).</param>
    /// <param name="validateIntegrity">Whether to perform checksum validation.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the file operation.</returns>
    Task<FileOperationResult> ExecuteFileOperationAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        bool validateIntegrity = true,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates checksum for a file.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="algorithm">Checksum algorithm to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hexadecimal checksum string.</returns>
    Task<string> CalculateChecksumAsync(
        string filePath,
        ChecksumAlgorithm algorithm = ChecksumAlgorithm.SHA256,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that source and destination files have matching checksums.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Destination file path.</param>
    /// <param name="algorithm">Checksum algorithm to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if checksums match, false otherwise.</returns>
    Task<bool> ValidateIntegrityAsync(
        string sourcePath,
        string destinationPath,
        ChecksumAlgorithm algorithm = ChecksumAlgorithm.SHA256,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Algorithm to use for checksum calculation.
/// </summary>
public enum ChecksumAlgorithm
{
    /// <summary>
    /// MD5 hash algorithm (128-bit, faster but less secure).
    /// </summary>
    MD5,

    /// <summary>
    /// SHA-256 hash algorithm (256-bit, slower but more secure).
    /// </summary>
    SHA256
}
