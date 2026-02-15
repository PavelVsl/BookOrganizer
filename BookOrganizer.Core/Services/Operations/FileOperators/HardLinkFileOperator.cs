using System.Runtime.InteropServices;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations.FileOperators;

/// <summary>
/// Implements hard link creation for files.
/// Hard links create multiple directory entries pointing to the same physical file on disk.
/// Note: Hard links only work within the same filesystem/volume.
/// </summary>
public class HardLinkFileOperator : ISpecificFileOperator
{
    private readonly ILogger<HardLinkFileOperator> _logger;

    public HardLinkFileOperator(ILogger<HardLinkFileOperator> logger)
    {
        _logger = logger;
    }

    public FileOperationType OperationType => FileOperationType.HardLink;

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

        if (!CanExecute(sourcePath, destinationPath))
        {
            throw new NotSupportedException(
                $"Cannot create hard link from {sourcePath} to {destinationPath}. " +
                "Hard links only work within the same filesystem/volume.");
        }

        _logger.LogDebug(
            "Creating hard link: {Source} -> {Destination}",
            sourcePath,
            destinationPath);

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

        // Create the hard link
        bool success;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            success = CreateHardLinkWindows(destinationPath, sourcePath);
        }
        else
        {
            success = CreateHardLinkUnix(sourcePath, destinationPath);
        }

        if (!success)
        {
            var errorMessage = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? $"Failed to create hard link. Error code: {Marshal.GetLastWin32Error()}"
                : "Failed to create hard link using 'link' system call.";
            throw new IOException(errorMessage);
        }

        // Report completion (hard links are instant, no transfer needed)
        var fileSize = new FileInfo(sourcePath).Length;
        progress?.Report(new FileOperationProgress
        {
            BytesProcessed = fileSize,
            TotalBytes = fileSize,
            Stage = OperationStage.Completed,
            CurrentFile = sourcePath
        });

        _logger.LogInformation(
            "Successfully created hard link: {Source} -> {Destination}",
            sourcePath,
            destinationPath);

        await Task.CompletedTask;
    }

    public bool CanExecute(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        // Hard links only work on the same volume/filesystem
        try
        {
            var sourceRoot = Path.GetPathRoot(Path.GetFullPath(sourcePath));
            var destRoot = Path.GetPathRoot(Path.GetFullPath(destinationPath));

            // On Unix, we need to check if they're on the same filesystem
            // For simplicity, we just check if the root paths match
            return string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string GetOperationDescription()
    {
        return "Creates hard links to files in the new location. " +
               "Files remain in original location, no additional disk space used. " +
               "Both paths point to the same physical file on disk. " +
               "Note: Hard links only work within the same filesystem/volume.";
    }

    /// <summary>
    /// Creates a hard link on Windows using P/Invoke.
    /// </summary>
    private static bool CreateHardLinkWindows(string linkPath, string targetPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        return CreateHardLink(linkPath, targetPath, IntPtr.Zero);
    }

    /// <summary>
    /// Creates a hard link on Unix/Linux/macOS using File.CreateSymbolicLink (fallback to manual).
    /// </summary>
    private static bool CreateHardLinkUnix(string targetPath, string linkPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            // .NET doesn't have built-in hard link support, so we shell out
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ln",
                Arguments = $"\"{targetPath}\" \"{linkPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Windows P/Invoke for CreateHardLink
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}
