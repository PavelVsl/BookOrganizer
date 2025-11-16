using BookOrganizer.Infrastructure.Database;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BookOrganizer.Services.Library;

/// <summary>
/// Builds and queries an in-memory library tree structure from SQLite database.
/// Provides normalized grouping of audiobooks by author/series/title.
/// </summary>
public class LibraryTree : ILibraryTree
{
    private readonly ILibraryDatabase _database;
    private readonly ILogger<LibraryTree> _logger;
    private readonly Dictionary<string, AuthorNode> _authorsByNormalizedName;
    private bool _isBuilt;

    public LibraryTree(ILibraryDatabase database, ILogger<LibraryTree> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authorsByNormalizedName = new Dictionary<string, AuthorNode>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task BuildFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building library tree from database");
        _authorsByNormalizedName.Clear();

        // Load library books (existing books in destination)
        var libraryBooks = await _database.GetLibraryBooksAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug("Loaded {Count} library books", libraryBooks.Count);

        foreach (var book in libraryBooks)
        {
            AddBookToTree(
                normalizedAuthor: book.NormalizedAuthor,
                displayAuthor: book.DisplayAuthor,
                normalizedTitle: book.NormalizedTitle,
                displayTitle: book.DisplayTitle,
                normalizedSeries: book.NormalizedSeries,
                displaySeries: book.DisplaySeries,
                seriesNumber: book.SeriesNumber,
                path: book.Path,
                destinationPath: null,
                sizeBytes: book.SizeBytes,
                fileCount: book.FileCount,
                metadataJson: book.MetadataJson,
                source: BookSource.Library);
        }

        // Load source books (books to be organized)
        var sourceBooks = await _database.GetSourceBooksAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug("Loaded {Count} source books", sourceBooks.Count);

        foreach (var book in sourceBooks)
        {
            AddBookToTree(
                normalizedAuthor: book.NormalizedAuthor,
                displayAuthor: book.DisplayAuthor,
                normalizedTitle: book.NormalizedTitle,
                displayTitle: book.DisplayTitle,
                normalizedSeries: book.NormalizedSeries,
                displaySeries: book.DisplaySeries,
                seriesNumber: book.SeriesNumber,
                path: book.SourcePath,
                destinationPath: book.DestinationPath,
                sizeBytes: book.SizeBytes,
                fileCount: book.FileCount,
                metadataJson: book.MetadataJson,
                source: BookSource.Source);
        }

        _isBuilt = true;
        _logger.LogInformation("Library tree built with {AuthorCount} authors and {BookCount} books",
            _authorsByNormalizedName.Count,
            _authorsByNormalizedName.Values.Sum(a => a.BookCount));
    }

    public IReadOnlyList<AuthorNode> GetNormalizedStructure()
    {
        EnsureBuilt();
        return _authorsByNormalizedName.Values
            .OrderBy(a => a.NormalizedAuthor, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<BookNode> FindMatchingLibraryBooks(
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries = null)
    {
        EnsureBuilt();

        // Find author node
        if (!_authorsByNormalizedName.TryGetValue(normalizedAuthor, out var authorNode))
        {
            return Array.Empty<BookNode>();
        }

        // Find library books with matching normalized title and series
        var matches = authorNode.Books
            .Where(b => b.Source == BookSource.Library)
            .Where(b => string.Equals(b.NormalizedTitle, normalizedTitle, StringComparison.OrdinalIgnoreCase))
            .Where(b => normalizedSeries == null ||
                       string.Equals(b.NormalizedSeries, normalizedSeries, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.AsReadOnly();
    }

    public string? GetNormalizedAuthor(string displayAuthor)
    {
        EnsureBuilt();

        // Try to find exact match by display author
        var author = _authorsByNormalizedName.Values
            .FirstOrDefault(a => string.Equals(a.DisplayAuthor, displayAuthor, StringComparison.OrdinalIgnoreCase));

        return author?.NormalizedAuthor;
    }

    public IReadOnlyList<BookNode> GetBooksByAuthor(string normalizedAuthor)
    {
        EnsureBuilt();

        if (_authorsByNormalizedName.TryGetValue(normalizedAuthor, out var authorNode))
        {
            return authorNode.Books.AsReadOnly();
        }

        return Array.Empty<BookNode>();
    }

    private void AddBookToTree(
        string normalizedAuthor,
        string displayAuthor,
        string normalizedTitle,
        string displayTitle,
        string? normalizedSeries,
        string? displaySeries,
        string? seriesNumber,
        string path,
        string? destinationPath,
        long sizeBytes,
        int fileCount,
        string metadataJson,
        BookSource source)
    {
        // Get or create author node
        if (!_authorsByNormalizedName.TryGetValue(normalizedAuthor, out var authorNode))
        {
            authorNode = new AuthorNode
            {
                NormalizedAuthor = normalizedAuthor,
                DisplayAuthor = displayAuthor,
                Books = new List<BookNode>()
            };
            _authorsByNormalizedName[normalizedAuthor] = authorNode;
        }

        // Deserialize metadata
        var metadata = JsonSerializer.Deserialize<BookMetadata>(metadataJson)
            ?? throw new InvalidOperationException($"Failed to deserialize metadata for {path}");

        // Create book node
        var bookNode = new BookNode
        {
            NormalizedTitle = normalizedTitle,
            NormalizedSeries = normalizedSeries,
            DisplayTitle = displayTitle,
            DisplaySeries = displaySeries,
            SeriesNumber = seriesNumber,
            SourcePath = source == BookSource.Source ? path : null,
            LibraryPath = source == BookSource.Library ? path : null,
            DestinationPath = destinationPath,
            Metadata = metadata,
            Source = source,
            SizeBytes = sizeBytes,
            FileCount = fileCount
        };

        authorNode.Books.Add(bookNode);
    }

    private void EnsureBuilt()
    {
        if (!_isBuilt)
        {
            throw new InvalidOperationException(
                "Library tree has not been built. Call BuildFromDatabaseAsync first.");
        }
    }
}
