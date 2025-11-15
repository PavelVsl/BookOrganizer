using BookOrganizer.Models;

namespace BookOrganizer.Services.Scanning;

/// <summary>
/// Service for scanning directories and detecting audiobook folders.
/// </summary>
public interface IDirectoryScanner
{
    /// <summary>
    /// Scans a directory recursively to find audiobook folders.
    /// </summary>
    /// <param name="sourcePath">Root directory to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of detected audiobook folders.</returns>
    Task<IReadOnlyList<AudiobookFolder>> ScanDirectoryAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
