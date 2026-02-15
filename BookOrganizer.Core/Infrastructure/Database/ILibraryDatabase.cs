using BookOrganizer.Models;

namespace BookOrganizer.Infrastructure.Database;

/// <summary>
/// Interface for library metadata database operations.
/// </summary>
public interface ILibraryDatabase : IDisposable
{
    /// <summary>
    /// Initializes the database, creating schema if needed.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all source books from the database (temporary data).
    /// </summary>
    Task ClearSourceBooksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all library books from the database, forcing a rebuild.
    /// </summary>
    Task ClearLibraryBooksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a library book in the cache.
    /// </summary>
    Task UpsertLibraryBookAsync(
        AudiobookFolder folder,
        BookMetadata metadata,
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a source book to the database for the current operation.
    /// </summary>
    Task AddSourceBookAsync(
        AudiobookFolder folder,
        BookMetadata metadata,
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries,
        string? destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all library books, optionally filtered by normalized author.
    /// </summary>
    Task<List<LibraryBookEntry>> GetLibraryBooksAsync(
        string? normalizedAuthor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a book exists in the library by normalized author and title.
    /// </summary>
    Task<bool> ExistsInLibraryAsync(
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all unique authors from the library (normalized and display names).
    /// </summary>
    Task<List<AuthorEntry>> GetAllAuthorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cache metadata by key.
    /// </summary>
    Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets cache metadata.
    /// </summary>
    Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all source books from the database (temporary data for current operation).
    /// </summary>
    Task<List<SourceBookEntry>> GetSourceBooksAsync(
        string? normalizedAuthor = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a library book entry from the database.
/// </summary>
public record LibraryBookEntry(
    int Id,
    string NormalizedAuthor,
    string NormalizedTitle,
    string? NormalizedSeries,
    string? SeriesNumber,
    string DisplayAuthor,
    string DisplayTitle,
    string? DisplaySeries,
    string Path,
    DateTime LastModified,
    long SizeBytes,
    int? DurationSeconds,
    int FileCount,
    string MetadataJson);

/// <summary>
/// Represents a source book entry from the database (temporary data).
/// </summary>
public record SourceBookEntry(
    int Id,
    string NormalizedAuthor,
    string NormalizedTitle,
    string? NormalizedSeries,
    string? SeriesNumber,
    string DisplayAuthor,
    string DisplayTitle,
    string? DisplaySeries,
    string SourcePath,
    string? DestinationPath,
    long SizeBytes,
    int? DurationSeconds,
    int FileCount,
    string MetadataJson);

/// <summary>
/// Represents an author entry (normalized + display name).
/// </summary>
public record AuthorEntry(
    string NormalizedAuthor,
    string DisplayAuthor,
    int BookCount);
