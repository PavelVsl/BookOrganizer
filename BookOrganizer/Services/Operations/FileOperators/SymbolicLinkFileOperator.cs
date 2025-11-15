using System.Runtime.InteropServices;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations.FileOperators;

/// <summary>
/// Implements symbolic link (symlink) creation for files.
/// Symbolic links are pointers to files that can span different filesystems/volumes.
/// Note: Requires appropriate permissions (admin/elevated on Windows).
/// </summary>
public class SymbolicLinkFileOperator : ISpecificFileOperator
{
    private readonly ILogger<SymbolicLinkFileOperator> _logger;

    public SymbolicLinkFileOperator(ILogger<SymbolicLinkFileOperator> logger)
    {
        _logger = logger;
    }

    public FileOperationType OperationType => FileOperationType.SymbolicLink;

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
            "Creating symbolic link: {Destination} -> {Source}",
            destinationPath,
            sourcePath);

        // Create destination directory if needed
        var destDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDirectory) && !Directory.Exists(destDirectory))
        {
            Directory.CreateDirectory(destDirectory);
        }

        // Report preparation stage
        progress?.Report(new FileOperationProgress
        {
            BytesProcessed = 0,
            TotalBytes = new FileInfo(sourcePath).Length,
            Stage = OperationStage.Preparing,
            CurrentFile = sourcePath
        });

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Use .NET 6+ built-in symbolic link support
            File.CreateSymbolicLink(destinationPath, sourcePath);

            // Report completion (symlinks are instant, no transfer needed)
            var fileSize = new FileInfo(sourcePath).Length;
            progress?.Report(new FileOperationProgress
            {
                BytesProcessed = fileSize,
                TotalBytes = fileSize,
                Stage = OperationStage.Completed,
                CurrentFile = sourcePath
            });

            _logger.LogInformation(
                "Successfully created symbolic link: {Destination} -> {Source}",
                destinationPath,
                sourcePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            var platformHint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "On Windows, creating symbolic links requires administrator privileges or Developer Mode enabled."
                : "On Unix/Linux, ensure you have permission to create symbolic links in the target directory.";

            _logger.LogError(
                ex,
                "Failed to create symbolic link due to insufficient permissions. {Hint}",
                platformHint);

            throw new UnauthorizedAccessException(
                $"Failed to create symbolic link: {ex.Message}. {platformHint}",
                ex);
        }
        catch (IOException ex)
        {
            _logger.LogError(
                ex,
                "Failed to create symbolic link: {Message}",
                ex.Message);
            throw;
        }

        await Task.CompletedTask;
    }

    public bool CanExecute(string sourcePath, string destinationPath)
    {
        // Symbolic links can work across volumes, so just check if source exists
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        // On Windows, check if we have permissions (simplified check)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // We can't easily check permissions without trying, so return true
            // The actual operation will fail with UnauthorizedAccessException if insufficient permissions
            return true;
        }

        // On Unix, symlinks generally work without special permissions
        return true;
    }

    public string GetOperationDescription()
    {
        var platformNote = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Requires administrator privileges or Developer Mode on Windows."
            : "Generally works without special permissions on Unix/Linux/macOS.";

        return "Creates symbolic links (symlinks) to files in the new location. " +
               "Files remain in original location, minimal disk space used. " +
               "Links can span different filesystems/volumes. " +
               $"Note: {platformNote}";
    }
}
