using BookOrganizer.Models;
using BookOrganizer.Services.Operations;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// Publishes audiobooks to an Audiobookshelf library folder by copying files
/// and creating .published marker files.
/// </summary>
public class PublishingService : IPublishingService
{
    private readonly IPathGenerator _pathGenerator;
    private readonly ILogger<PublishingService> _logger;

    private const string PublishedMarkerFile = ".published";

    public PublishingService(IPathGenerator pathGenerator, ILogger<PublishingService> logger)
    {
        _pathGenerator = pathGenerator;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsPublished(string bookFolderPath)
    {
        return File.Exists(Path.Combine(bookFolderPath, PublishedMarkerFile));
    }

    /// <inheritdoc />
    public async Task<PublishResult> PublishBookAsync(
        string bookFolderPath,
        BookMetadata metadata,
        string absLibraryFolder,
        CancellationToken ct)
    {
        try
        {
            if (IsPublished(bookFolderPath))
            {
                return new PublishResult(false, bookFolderPath, null, "Already published");
            }

            // Generate target path using same structure as local library
            var targetPath = _pathGenerator.GenerateTargetPath(metadata, absLibraryFolder);

            if (Directory.Exists(targetPath))
            {
                return new PublishResult(false, bookFolderPath, targetPath, "Target folder already exists in ABS library");
            }

            // Copy the entire folder
            _logger.LogInformation("Publishing {Source} -> {Target}", bookFolderPath, targetPath);
            CopyDirectory(bookFolderPath, targetPath, ct);

            // Create .published marker in source folder
            var markerContent = $"published_at: {DateTime.UtcNow:O}\ntarget: {targetPath}\n";
            await File.WriteAllTextAsync(
                Path.Combine(bookFolderPath, PublishedMarkerFile),
                markerContent, ct).ConfigureAwait(false);

            _logger.LogInformation("Published successfully: {Title} by {Author}", metadata.Title, metadata.Author);
            return new PublishResult(true, bookFolderPath, targetPath, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {Path}", bookFolderPath);
            return new PublishResult(false, bookFolderPath, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<List<PublishResult>> PublishBooksAsync(
        IReadOnlyList<(string Path, BookMetadata Metadata)> books,
        string absLibraryFolder,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        var results = new List<PublishResult>();
        var completed = 0;

        foreach (var (path, metadata) in books)
        {
            ct.ThrowIfCancellationRequested();
            var result = await PublishBookAsync(path, metadata, absLibraryFolder, ct).ConfigureAwait(false);
            results.Add(result);
            completed++;
            progress?.Report(completed);
        }

        return results;
    }

    private static void CopyDirectory(string sourceDir, string targetDir, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);

        // Copy files (skip .published marker and other dot-files)
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith('.'))
                continue;

            File.Copy(file, Path.Combine(targetDir, fileName), overwrite: false);
        }

        // Copy subdirectories recursively (for multi-disc books)
        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(subDir);
            if (dirName.StartsWith('.'))
                continue;

            CopyDirectory(subDir, Path.Combine(targetDir, dirName), ct);
        }
    }
}
