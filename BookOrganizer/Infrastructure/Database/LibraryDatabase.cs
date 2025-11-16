using BookOrganizer.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BookOrganizer.Infrastructure.Database;

/// <summary>
/// SQLite-based library metadata database.
/// </summary>
public class LibraryDatabase : ILibraryDatabase
{
    private readonly string _databasePath;
    private readonly ILogger<LibraryDatabase> _logger;
    private SqliteConnection? _connection;
    private bool _disposed;

    public LibraryDatabase(string libraryRoot, ILogger<LibraryDatabase> logger)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            throw new ArgumentException("Library root cannot be null or empty", nameof(libraryRoot));
        }

        _logger = logger;

        // Create .bookorganizer directory if it doesn't exist
        var dbDirectory = Path.Combine(libraryRoot, LibraryDatabaseSchema.DatabaseFolder);
        Directory.CreateDirectory(dbDirectory);

        _databasePath = Path.Combine(dbDirectory, LibraryDatabaseSchema.DatabaseFileName);
        _logger.LogDebug("Library database path: {Path}", _databasePath);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection != null)
        {
            _logger.LogDebug("Database already initialized");
            return;
        }

        _logger.LogInformation("Initializing library database at {Path}", _databasePath);

        _connection = new SqliteConnection($"Data Source={_databasePath}");
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        // Create schema
        foreach (var statement in LibraryDatabaseSchema.GetCreateStatements())
        {
            using var command = _connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Library database initialized successfully");
    }

    public async Task ClearSourceBooksAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        using var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM source_books";
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Cleared {Count} source books", deleted);
    }

    public async Task ClearLibraryBooksAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        using var command = _connection!.CreateCommand();
        command.CommandText = "DELETE FROM library_books";
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Cleared {Count} library books from cache", deleted);
    }

    public async Task UpsertLibraryBookAsync(
        AudiobookFolder folder,
        BookMetadata metadata,
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var metadataJson = JsonSerializer.Serialize(metadata);
        var lastModified = Directory.GetLastWriteTimeUtc(folder.Path);

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT INTO library_books (
                normalized_author, normalized_title, normalized_series, series_number,
                display_author, display_title, display_series,
                path, last_modified, size_bytes, duration_seconds, file_count, metadata_json
            )
            VALUES (
                @normalized_author, @normalized_title, @normalized_series, @series_number,
                @display_author, @display_title, @display_series,
                @path, @last_modified, @size_bytes, @duration_seconds, @file_count, @metadata_json
            )
            ON CONFLICT(path) DO UPDATE SET
                normalized_author = excluded.normalized_author,
                normalized_title = excluded.normalized_title,
                normalized_series = excluded.normalized_series,
                series_number = excluded.series_number,
                display_author = excluded.display_author,
                display_title = excluded.display_title,
                display_series = excluded.display_series,
                last_modified = excluded.last_modified,
                size_bytes = excluded.size_bytes,
                duration_seconds = excluded.duration_seconds,
                file_count = excluded.file_count,
                metadata_json = excluded.metadata_json";

        command.Parameters.AddWithValue("@normalized_author", normalizedAuthor);
        command.Parameters.AddWithValue("@normalized_title", normalizedTitle);
        command.Parameters.AddWithValue("@normalized_series", normalizedSeries ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@series_number", metadata.SeriesNumber ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@display_author", metadata.Author ?? "Unknown Author");
        command.Parameters.AddWithValue("@display_title", metadata.Title);
        command.Parameters.AddWithValue("@display_series", metadata.Series ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@path", folder.Path);
        command.Parameters.AddWithValue("@last_modified", lastModified.ToString("o"));
        command.Parameters.AddWithValue("@size_bytes", folder.TotalSizeBytes);
        command.Parameters.AddWithValue("@duration_seconds", DBNull.Value); // TODO: Calculate from audio files
        command.Parameters.AddWithValue("@file_count", folder.AudioFiles.Count);
        command.Parameters.AddWithValue("@metadata_json", metadataJson);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddSourceBookAsync(
        AudiobookFolder folder,
        BookMetadata metadata,
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries,
        string? destinationPath,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var metadataJson = JsonSerializer.Serialize(metadata);

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO source_books (
                normalized_author, normalized_title, normalized_series, series_number,
                display_author, display_title, display_series,
                source_path, destination_path, size_bytes, duration_seconds, file_count, metadata_json
            )
            VALUES (
                @normalized_author, @normalized_title, @normalized_series, @series_number,
                @display_author, @display_title, @display_series,
                @source_path, @destination_path, @size_bytes, @duration_seconds, @file_count, @metadata_json
            )";

        command.Parameters.AddWithValue("@normalized_author", normalizedAuthor);
        command.Parameters.AddWithValue("@normalized_title", normalizedTitle);
        command.Parameters.AddWithValue("@normalized_series", normalizedSeries ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@series_number", metadata.SeriesNumber ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@display_author", metadata.Author ?? "Unknown Author");
        command.Parameters.AddWithValue("@display_title", metadata.Title);
        command.Parameters.AddWithValue("@display_series", metadata.Series ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@source_path", folder.Path);
        command.Parameters.AddWithValue("@destination_path", destinationPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@size_bytes", folder.TotalSizeBytes);
        command.Parameters.AddWithValue("@duration_seconds", DBNull.Value); // TODO: Calculate from audio files
        command.Parameters.AddWithValue("@file_count", folder.AudioFiles.Count);
        command.Parameters.AddWithValue("@metadata_json", metadataJson);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<LibraryBookEntry>> GetLibraryBooksAsync(
        string? normalizedAuthor = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var books = new List<LibraryBookEntry>();

        using var command = _connection!.CreateCommand();
        command.CommandText = normalizedAuthor == null
            ? "SELECT * FROM library_books ORDER BY normalized_author, normalized_title"
            : "SELECT * FROM library_books WHERE normalized_author = @author ORDER BY normalized_title";

        if (normalizedAuthor != null)
        {
            command.Parameters.AddWithValue("@author", normalizedAuthor);
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            books.Add(new LibraryBookEntry(
                Id: reader.GetInt32(0),
                NormalizedAuthor: reader.GetString(1),
                NormalizedTitle: reader.GetString(2),
                NormalizedSeries: reader.IsDBNull(3) ? null : reader.GetString(3),
                SeriesNumber: reader.IsDBNull(4) ? null : reader.GetString(4),
                DisplayAuthor: reader.GetString(5),
                DisplayTitle: reader.GetString(6),
                DisplaySeries: reader.IsDBNull(7) ? null : reader.GetString(7),
                Path: reader.GetString(8),
                LastModified: DateTime.Parse(reader.GetString(9)),
                SizeBytes: reader.GetInt64(10),
                DurationSeconds: reader.IsDBNull(11) ? null : reader.GetInt32(11),
                FileCount: reader.GetInt32(12),
                MetadataJson: reader.GetString(13)
            ));
        }

        return books;
    }

    public async Task<bool> ExistsInLibraryAsync(
        string normalizedAuthor,
        string normalizedTitle,
        string? normalizedSeries = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        using var command = _connection!.CreateCommand();

        if (normalizedSeries == null)
        {
            command.CommandText = @"
                SELECT COUNT(*) FROM library_books
                WHERE normalized_author = @author AND normalized_title = @title";
            command.Parameters.AddWithValue("@author", normalizedAuthor);
            command.Parameters.AddWithValue("@title", normalizedTitle);
        }
        else
        {
            command.CommandText = @"
                SELECT COUNT(*) FROM library_books
                WHERE normalized_author = @author AND normalized_title = @title AND normalized_series = @series";
            command.Parameters.AddWithValue("@author", normalizedAuthor);
            command.Parameters.AddWithValue("@title", normalizedTitle);
            command.Parameters.AddWithValue("@series", normalizedSeries);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result) > 0;
    }

    public async Task<List<AuthorEntry>> GetAllAuthorsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var authors = new List<AuthorEntry>();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            SELECT normalized_author, display_author, COUNT(*) as book_count
            FROM library_books
            GROUP BY normalized_author
            ORDER BY normalized_author";

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            authors.Add(new AuthorEntry(
                NormalizedAuthor: reader.GetString(0),
                DisplayAuthor: reader.GetString(1),
                BookCount: reader.GetInt32(2)
            ));
        }

        return authors;
    }

    public async Task<List<SourceBookEntry>> GetSourceBooksAsync(
        string? normalizedAuthor = null,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var books = new List<SourceBookEntry>();

        using var command = _connection!.CreateCommand();
        command.CommandText = normalizedAuthor == null
            ? "SELECT * FROM source_books ORDER BY normalized_author, normalized_title"
            : "SELECT * FROM source_books WHERE normalized_author = @author ORDER BY normalized_title";

        if (normalizedAuthor != null)
        {
            command.Parameters.AddWithValue("@author", normalizedAuthor);
        }

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            books.Add(new SourceBookEntry(
                Id: reader.GetInt32(0),
                NormalizedAuthor: reader.GetString(1),
                NormalizedTitle: reader.GetString(2),
                NormalizedSeries: reader.IsDBNull(3) ? null : reader.GetString(3),
                SeriesNumber: reader.IsDBNull(4) ? null : reader.GetString(4),
                DisplayAuthor: reader.GetString(5),
                DisplayTitle: reader.GetString(6),
                DisplaySeries: reader.IsDBNull(7) ? null : reader.GetString(7),
                SourcePath: reader.GetString(8),
                DestinationPath: reader.IsDBNull(9) ? null : reader.GetString(9),
                SizeBytes: reader.GetInt64(10),
                DurationSeconds: reader.IsDBNull(11) ? null : reader.GetInt32(11),
                FileCount: reader.GetInt32(12),
                MetadataJson: reader.GetString(13)
            ));
        }

        return books;
    }

    public async Task<string?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT value FROM cache_metadata WHERE key = @key";
        command.Parameters.AddWithValue("@key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result?.ToString();
    }

    public async Task SetMetadataAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        using var command = _connection!.CreateCommand();
        command.CommandText = @"
            INSERT OR REPLACE INTO cache_metadata (key, value, updated_at)
            VALUES (@key, @value, CURRENT_TIMESTAMP)";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private void EnsureInitialized()
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _connection?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
