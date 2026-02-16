using System.Text.RegularExpressions;
using BookOrganizer.Infrastructure.Exceptions;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Scanning;

/// <summary>
/// Service for scanning directories and detecting audiobook folders containing audio files.
/// </summary>
public class DirectoryScanner : IDirectoryScanner
{
    private readonly ILogger<DirectoryScanner> _logger;
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".m4b", ".flac", ".aac", ".ogg", ".opus", ".wma"
    };

    private static readonly Regex DiscFolderPattern = new(
        @"^(?:Disc|CD|Disk)\s*\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public DirectoryScanner(ILogger<DirectoryScanner> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AudiobookFolder>> ScanDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        return ScanDirectoryAsync(sourcePath, progress: null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudiobookFolder>> ScanDirectoryAsync(
        string sourcePath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryScanningException(
                $"Source directory does not exist: {sourcePath}",
                sourcePath);
        }

        _logger.LogInformation("Starting directory scan: {SourcePath}", sourcePath);

        var audiobookFolders = new List<AudiobookFolder>();

        try
        {
            var directoriesScanned = await ScanDirectoryRecursiveAsync(
                sourcePath,
                audiobookFolders,
                progress,
                0,
                cancellationToken);

            _logger.LogInformation(
                "Scan complete. Found {Count} audiobook folders in {DirCount} directories",
                audiobookFolders.Count,
                directoriesScanned);

            // Report completion
            progress?.Report(new ScanProgress
            {
                DirectoriesScanned = directoriesScanned,
                AudiobookFoldersFound = audiobookFolders.Count,
                AudioFilesFound = audiobookFolders.Sum(f => f.FileCount),
                IsComplete = true
            });

            return audiobookFolders;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Directory scan cancelled");
            throw;
        }
        catch (DirectoryScanningException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory: {SourcePath}", sourcePath);
            throw new DirectoryScanningException(
                $"Failed to scan directory: {ex.Message}",
                sourcePath,
                ex);
        }
    }

    private async Task<int> ScanDirectoryRecursiveAsync(
        string currentPath,
        List<AudiobookFolder> results,
        IProgress<ScanProgress>? progress,
        int directoriesScanned,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        directoriesScanned++;

        // Report progress every 10 directories
        if (directoriesScanned % 10 == 0)
        {
            progress?.Report(new ScanProgress
            {
                DirectoriesScanned = directoriesScanned,
                AudiobookFoldersFound = results.Count,
                AudioFilesFound = results.Sum(f => f.FileCount),
                CurrentDirectory = currentPath,
                IsComplete = false
            });
        }

        try
        {
            // Get all files in current directory
            var allFiles = Directory.EnumerateFiles(currentPath).ToList();

            // Separate audio files from other files
            var audioFiles = allFiles.Where(file => IsAudioFile(file)).ToList();
            var otherFiles = allFiles.Where(file => !IsAudioFile(file)).ToList();

            // Check subdirectories for disc folder pattern
            var subdirectories = Directory.EnumerateDirectories(currentPath).ToList();
            var discSubdirs = subdirectories
                .Where(d => DiscFolderPattern.IsMatch(Path.GetFileName(d)))
                .ToList();
            var nonDiscSubdirs = subdirectories
                .Where(d => !DiscFolderPattern.IsMatch(Path.GetFileName(d)))
                .ToList();

            // Check if disc subfolders contain audio files
            var discFolderNames = new List<string>();
            var discAudioFiles = new List<string>();
            var discOtherFiles = new List<string>();

            foreach (var discDir in discSubdirs)
            {
                var discFiles = Directory.EnumerateFiles(discDir).ToList();
                var discAudio = discFiles.Where(IsAudioFile).ToList();

                if (discAudio.Count > 0)
                {
                    discFolderNames.Add(Path.GetFileName(discDir));
                    discAudioFiles.AddRange(discAudio);
                    discOtherFiles.AddRange(discFiles.Where(f => !IsAudioFile(f)));
                    directoriesScanned++;
                }
                else
                {
                    // No audio files in disc subfolder - treat as regular subdirectory
                    nonDiscSubdirs.Add(discDir);
                }
            }

            // If we found disc subfolders with audio, create a single multi-disc AudiobookFolder
            if (discFolderNames.Count > 0)
            {
                // Include root-level audio files first, then disc subfolder files
                var allAudioFiles = audioFiles.Concat(discAudioFiles).ToList();
                var allOtherFiles = otherFiles.Concat(discOtherFiles).ToList();

                var totalSize = allAudioFiles.Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get file size: {FilePath}", file);
                        return 0L;
                    }
                });

                var audiobookFolder = new AudiobookFolder
                {
                    Path = currentPath,
                    AudioFiles = allAudioFiles,
                    OtherFiles = allOtherFiles,
                    TotalSizeBytes = totalSize,
                    DiscSubfolders = discFolderNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                };

                results.Add(audiobookFolder);

                _logger.LogDebug(
                    "Found multi-disc audiobook: {Path} ({DiscCount} discs, {FileCount} files, {SizeMB:F2} MB)",
                    currentPath,
                    discFolderNames.Count,
                    allAudioFiles.Count,
                    totalSize / 1024.0 / 1024.0);
            }
            else if (audioFiles.Count > 0)
            {
                // Single-disc audiobook (original behavior)
                var totalSize = audioFiles.Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get file size: {FilePath}", file);
                        return 0L;
                    }
                });

                var audiobookFolder = new AudiobookFolder
                {
                    Path = currentPath,
                    AudioFiles = audioFiles,
                    OtherFiles = otherFiles,
                    TotalSizeBytes = totalSize
                };

                results.Add(audiobookFolder);

                _logger.LogDebug(
                    "Found audiobook folder: {Path} ({FileCount} files, {SizeMB:F2} MB)",
                    currentPath,
                    audioFiles.Count,
                    totalSize / 1024.0 / 1024.0);
            }

            // Recursively scan non-disc subdirectories (skip hidden/dot-directories like .trash)
            foreach (var subdirectory in nonDiscSubdirs.Where(d => !Path.GetFileName(d).StartsWith('.')))
            {
                directoriesScanned = await ScanDirectoryRecursiveAsync(
                    subdirectory,
                    results,
                    progress,
                    directoriesScanned,
                    cancellationToken);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to directory: {Path}", currentPath);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Directory not found: {Path}", currentPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory: {Path}", currentPath);
        }

        // Make this actually async to allow for cooperative cancellation
        await Task.Yield();

        return directoriesScanned;
    }

    private static bool IsAudioFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return AudioExtensions.Contains(extension);
    }
}
