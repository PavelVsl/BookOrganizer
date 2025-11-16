using BookOrganizer.Models;

namespace BookOrganizer.Services.Library;

/// <summary>
/// Interface for building and querying an in-memory library tree structure.
/// Provides normalized grouping of audiobooks by author/series/title using SQLite database backend.
/// </summary>
public interface ILibraryTree
{
    /// <summary>
    /// Builds the library tree from the SQLite database.
    /// Loads both existing library books and source books for the current operation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task BuildFromDatabaseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the normalized tree structure representing how books will be organized.
    /// Returns author nodes with nested book nodes using normalized metadata for grouping.
    /// </summary>
    /// <returns>Collection of author nodes with their books</returns>
    IReadOnlyList<AuthorNode> GetNormalizedStructure();

    /// <summary>
    /// Finds existing library books that match a given source book's normalized metadata.
    /// Used for duplicate detection - returns books with same normalized author/title/series.
    /// </summary>
    /// <param name="normalizedAuthor">Normalized author name</param>
    /// <param name="normalizedTitle">Normalized title</param>
    /// <param name="normalizedSeries">Normalized series (optional)</param>
    /// <returns>List of matching library book nodes</returns>
    IReadOnlyList<BookNode> FindMatchingLibraryBooks(
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries = null);

    /// <summary>
    /// Gets the normalized author name for a given display author name.
    /// Uses the tree's existing normalization for consistency.
    /// </summary>
    /// <param name="displayAuthor">Display author name from metadata</param>
    /// <returns>Normalized author name for grouping, or null if not found in tree</returns>
    string? GetNormalizedAuthor(string displayAuthor);

    /// <summary>
    /// Gets all books for a specific normalized author.
    /// </summary>
    /// <param name="normalizedAuthor">Normalized author name</param>
    /// <returns>Collection of book nodes for the author, or empty if author not found</returns>
    IReadOnlyList<BookNode> GetBooksByAuthor(string normalizedAuthor);
}
