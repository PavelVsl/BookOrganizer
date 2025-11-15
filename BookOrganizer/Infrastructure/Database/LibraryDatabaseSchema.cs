namespace BookOrganizer.Infrastructure.Database;

/// <summary>
/// SQL schema for the library metadata database.
/// </summary>
public static class LibraryDatabaseSchema
{
    public const string DatabaseFileName = "library.db";
    public const string DatabaseFolder = ".bookorganizer";

    public const int CurrentVersion = 1;

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

            // Library books table (scanned from destination)
            @"CREATE TABLE IF NOT EXISTS library_books (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
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
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            )",

            // Source books table (temporary, for current preview/organize operation)
            @"CREATE TABLE IF NOT EXISTS source_books (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
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
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            )",

            // Indexes for fast lookups
            "CREATE INDEX IF NOT EXISTS idx_library_books_normalized_author ON library_books(normalized_author)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_normalized_title ON library_books(normalized_title)",
            "CREATE INDEX IF NOT EXISTS idx_library_books_path ON library_books(path)",
            "CREATE INDEX IF NOT EXISTS idx_source_books_normalized_author ON source_books(normalized_author)",
            "CREATE INDEX IF NOT EXISTS idx_source_books_normalized_title ON source_books(normalized_title)",

            // Insert version metadata
            @"INSERT OR REPLACE INTO cache_metadata (key, value, updated_at)
              VALUES ('version', '1', CURRENT_TIMESTAMP)"
        };
    }
}
