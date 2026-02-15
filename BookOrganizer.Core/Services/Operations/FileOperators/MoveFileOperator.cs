using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations.FileOperators;

/// <summary>
/// Implements file move operation with streaming fallback for cross-volume moves.
/// </summary>
public class MoveFileOperator : ISpecificFileOperator
{
    private readonly ILogger<MoveFileOperator> _logger;
    private readonly CopyFileOperator _copyOperator;

    public MoveFileOperator(
        ILogger<MoveFileOperator> logger,
        CopyFileOperator copyOperator)
    {
        _logger = logger;
        _copyOperator = copyOperator;
    }

    public FileOperationType OperationType => FileOperationType.Move;

    public async Task ExecuteAsync(
        string sourcePath,
        string destinationPath,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source file not found: {sourcePath}", sourcePath);
        }

        _logger.LogDebug(
            "Moving file: {Source} -> {Destination}",
            sourcePath,
            destinationPath);

        // Create destination directory if needed
        var destDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDirectory) && !Directory.Exists(destDirectory))
        {
            Directory.CreateDirectory(destDirectory);
        }

        try
        {
            // Try fast move first (works only on same volume)
            File.Move(sourcePath, destinationPath, overwrite: false);

            _logger.LogInformation(
                "Successfully moved file (fast): {Source} -> {Destination}",
                sourcePath,
                destinationPath);
        }
        catch (IOException ex) when (IsCrossVolumeError(ex))
        {
            // Fall back to copy + delete for cross-volume moves
            _logger.LogDebug(
                "Cross-volume move detected, using copy+delete: {Message}",
                ex.Message);

            // Copy file first
            await _copyOperator.ExecuteAsync(
                sourcePath,
                destinationPath,
                progress,
                cancellationToken).ConfigureAwait(false);

            // Delete source after successful copy
            File.Delete(sourcePath);

            _logger.LogInformation(
                "Successfully moved file (copy+delete): {Source} -> {Destination}",
                sourcePath,
                destinationPath);
        }
    }

    public bool CanExecute(string sourcePath, string destinationPath)
    {
        // Move can always work (assuming valid paths and permissions)
        return File.Exists(sourcePath);
    }

    public string GetOperationDescription()
    {
        return "Moves files to the new location, removing originals. Saves disk space but changes original location.";
    }

    /// <summary>
    /// Checks if the IOException is due to cross-volume move attempt.
    /// </summary>
    private static bool IsCrossVolumeError(IOException ex)
    {
        // Cross-volume moves throw IOException with HResult 0x80070011 (ERROR_NOT_SAME_DEVICE)
        // Or message contains "different" or "volume"
        return ex.HResult == unchecked((int)0x80070011) ||
               ex.Message.Contains("different", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("volume", StringComparison.OrdinalIgnoreCase);
    }
}
