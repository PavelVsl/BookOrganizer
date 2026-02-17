using BookOrganizer.Models;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// Service for publishing audiobooks to an Audiobookshelf library folder.
/// </summary>
public interface IPublishingService
{
    /// <summary>
    /// Checks whether a book has already been published (has a .published marker file).
    /// </summary>
    bool IsPublished(string bookFolderPath);

    /// <summary>
    /// Publishes a single book by copying its folder to the ABS library folder.
    /// Creates a .published marker file in the source folder on success.
    /// </summary>
    Task<PublishResult> PublishBookAsync(
        string bookFolderPath,
        BookMetadata metadata,
        string absLibraryFolder,
        CancellationToken ct);

    /// <summary>
    /// Publishes multiple books with progress reporting.
    /// </summary>
    Task<List<PublishResult>> PublishBooksAsync(
        IReadOnlyList<(string Path, BookMetadata Metadata)> books,
        string absLibraryFolder,
        IProgress<int>? progress,
        CancellationToken ct);
}
