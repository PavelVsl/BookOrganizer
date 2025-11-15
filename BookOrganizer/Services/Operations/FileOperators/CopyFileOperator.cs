using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations.FileOperators;

/// <summary>
/// Implements file copy operation with streaming and progress tracking.
/// </summary>
public class CopyFileOperator : ISpecificFileOperator
{
    private readonly ILogger<CopyFileOperator> _logger;
    private const int BufferSize = 4 * 1024 * 1024; // 4MB buffer for streaming

    public CopyFileOperator(ILogger<CopyFileOperator> logger)
    {
        _logger = logger;
    }

    public FileOperationType OperationType => FileOperationType.Copy;

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

        var fileInfo = new FileInfo(sourcePath);
        var totalBytes = fileInfo.Length;

        _logger.LogDebug(
            "Copying file: {Source} -> {Destination} ({Size} bytes)",
            sourcePath,
            destinationPath,
            totalBytes);

        // Create destination directory if needed
        var destDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDirectory) && !Directory.Exists(destDirectory))
        {
            Directory.CreateDirectory(destDirectory);
        }

        // Copy with streaming for large files
        using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var destStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[BufferSize];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            totalBytesRead += bytesRead;

            // Report progress
            progress?.Report(new FileOperationProgress
            {
                BytesProcessed = totalBytesRead,
                TotalBytes = totalBytes,
                Stage = OperationStage.TransferringFile,
                CurrentFile = sourcePath
            });

            cancellationToken.ThrowIfCancellationRequested();
        }

        // Ensure all data is written to disk
        await destStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Preserve timestamps from source file
        File.SetCreationTime(destinationPath, File.GetCreationTime(sourcePath));
        File.SetLastWriteTime(destinationPath, File.GetLastWriteTime(sourcePath));
        File.SetLastAccessTime(destinationPath, File.GetLastAccessTime(sourcePath));

        _logger.LogInformation(
            "Successfully copied file: {Source} -> {Destination}",
            sourcePath,
            destinationPath);
    }

    public bool CanExecute(string sourcePath, string destinationPath)
    {
        // Copy can always work (assuming valid paths and permissions)
        return File.Exists(sourcePath);
    }

    public string GetOperationDescription()
    {
        return "Copies files to the new location, keeping originals. Uses more disk space but provides full independence.";
    }
}
