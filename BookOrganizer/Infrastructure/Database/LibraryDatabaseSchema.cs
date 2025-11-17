namespace BookOrganizer.Infrastructure.Database;

/// <summary>
/// SQL schema for the library metadata database.
/// </summary>
public static class LibraryDatabaseSchema
{
    public const string DatabaseFileName = "library.db";
    public const string DatabaseFolder = ".bookorganizer";

    public const int CurrentVersion = 2;

    /// <summary>
    /// Gets the SQL statements to create the database schema.
    /// </summary>
    public static string[] GetCreateStatements()
    {
        return new[]
        {
            // Metadata table for versioning and cache info
            @"CREATE TABLE IF NOT EXISTS cache_metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            )",

            // Authors table (normalized)
            @"CREATE TABLE IF NOT EXISTS authors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                normalized_name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                display_name TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            )",

            // Series table (normalized)
            @"CREATE TABLE IF NOT EXISTS series (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                normalized_name TEXT NOT NULL COLLATE NOCASE,
                display_name TEXT NOT NULL,
                author_id INTEGER,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (author_id) REFERENCES authors(id) ON DELETE SET NULL,
                UNIQUE(normalized_name, author_id)
            )",

            // Library books table (scanned from destination)
            @"CREATE TABLE IF NOT EXISTS library_books (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                author_id INTEGER,
                series_id INTEGER,
                normalized_author TEXT NOT NULL COLLATE NOCASE,
                normalized_title TEXT NOT NULL COLLATE NOCASE,
                normalized_series TEXT COLLATE NOCASE,
                series_number TEXT,
                display_author TEXT NOT NULL,
                display_title TEXT NOT NULL,
                display_series TEXT,
                path TEXT NOT NULL UNIQUE,
                last_modified TEXT NOT NULL,
                size_bytes INTEGER NOT NULL,
                duration_seconds INTEGER,
                file_count INTEGER NOT NULL,
                metadata_json TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (author_id) REFERENCES authors(id) ON DELETE SET NULL,
                FOREIGN KEY (series_id) REFERENCES series(id) ON DELETE SET NULL
            )",

            // Source books table (temporary, for current preview/organize operation)
            @"CREATE TABLE IF NOT EXISTS source_books (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                author_id INTEGER,
                series_id INTEGER,
                normalized_author TEXT NOT NULL COLLATE NOCASE,
                normalized_title TEXT NOT NULL COLLATE NOCASE,
                normalized_series TEXT COLLATE NOCASE,
                series_number TEXT,
                display_author TEXT NOT NULL,
                display_title TEXT NOT NULL,
                display_series TEXT,
                source_path TEXT NOT NULL UNIQUE,
                destination_path TEXT,
                size_bytes INTEGER NOT NULL,
                duration_seconds INTEGER,
                file_count INTEGER NOT NULL,
                metadata_json TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (author_id) REFERENCES authors(id) ON DELETE SET NULL,
                FOREIGN KEY (series_id) REFERENCES series(id) ON DELETE SET NULL
            )",

            // Indexes for fast lookups
            "CREATE INDEX IF NOT EXISTS idx_authors_normalized_name ON authors(normalized_name)",
            "CREATE INDEX IF NOT EXISTS idx_series_normalized_name ON series(normalized_name)",
            "CREATE INDEX IF NOT EXISTS idx_series_author_id ON series(author_id)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_author_id ON library_books(author_id)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_series_id ON library_books(series_id)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_normalized_author ON library_books(normalized_author)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_normalized_title ON library_books(normalized_title)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_path ON library_books(path)",
            "CREATE INDEX IF NOT EXISTS idx_source_books_author_id ON source_books(author_id)",
            "CREATE INDEX IF NOT EXISTS idx_source_books_series_id ON source_books(series_id)",
            "CREATE INDEX IF NOT EXISTS idx_source_books_normalized_author ON source_books(normalized_author)",
            "CREATE INDEX IF NOT EXISTS idx_source_books_normalized_title ON source_books(normalized_title)",

            // Insert version metadata
            @"INSERT OR REPLACE INTO cache_metadata (key, value, updated_at)
              VALUES ('version', '2', CURRENT_TIMESTAMP)"
        };
    }
}
