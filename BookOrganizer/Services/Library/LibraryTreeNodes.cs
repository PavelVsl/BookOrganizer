using BookOrganizer.Models;

namespace BookOrganizer.Services.Library;

/// <summary>
/// Represents an author node in the library tree structure.
/// Contains normalized author key for grouping and display name for UI.
/// </summary>
public record AuthorNode
{
    /// <summary>
    /// Normalized author name used for grouping and SQL queries.
    /// </summary>
    public required string NormalizedAuthor { get; init; }

    /// <summary>
    /// Display author name from metadata (original formatting).
    /// </summary>
    public required string DisplayAuthor { get; init; }

    /// <summary>
    /// Collection of books by this author.
    /// </summary>
    public required List<BookNode> Books { get; init; }

    /// <summary>
    /// Total number of books by this author.
    /// </summary>
    public int BookCount => Books.Count;
}

/// <summary>
/// Represents a book node in the library tree structure.
/// Contains both normalized metadata for grouping and original metadata for operations.
/// </summary>
public record BookNode
{
    /// <summary>
    /// Normalized title used for grouping and duplicate detection.
    /// </summary>
    public required string NormalizedTitle { get; init; }

    /// <summary>
    /// Normalized series name (if book is part of a series).
    /// </summary>
    public string? NormalizedSeries { get; init; }

    /// <summary>
    /// Display title from metadata (original formatting).
    /// </summary>
    public required string DisplayTitle { get; init; }

    /// <summary>
    /// Display series name from metadata.
    /// </summary>
    public string? DisplaySeries { get; init; }

    /// <summary>
    /// Series number (if book is part of a series).
    /// </summary>
    public string? SeriesNumber { get; init; }

    /// <summary>
    /// Source path of the audiobook folder (for source books).
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Destination path where the book will be organized (for source books).
    /// </summary>
    public string? DestinationPath { get; init; }

    /// <summary>
    /// Path in the library (for existing library books).
    /// </summary>
    public string? LibraryPath { get; init; }

    /// <summary>
    /// Full metadata for the book.
    /// </summary>
    public required BookMetadata Metadata { get; init; }

    /// <summary>
    /// Indicates whether this is a source book (to be organized) or an existing library book.
    /// </summary>
    public required BookSource Source { get; init; }

    /// <summary>
    /// Total size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Number of audio files.
    /// </summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Indicates if this book is part of a series.
    /// </summary>
    public bool IsSeries => !string.IsNullOrEmpty(NormalizedSeries);
}

/// <summary>
/// Indicates the source of a book node.
/// </summary>
public enum BookSource
{
    /// <summary>
    /// Book from source directory (to be organized).
    /// </summary>
    Source,

    /// <summary>
    /// Book already in the library.
    /// </summary>
    Library
}
