namespace BookOrganizer.Models;

/// <summary>
/// Represents metadata in Audiobookshelf JSON format.
/// Compatible with Audiobookshelf server metadata.json files.
/// </summary>
public record AudiobookshelfMetadata
{
    /// <summary>
    /// Book title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Book subtitle (optional).
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Author name(s), comma-separated for multiple authors.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Narrator name(s), comma-separated for multiple narrators.
    /// </summary>
    public string? Narrator { get; init; }

    /// <summary>
    /// Publisher name.
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Publication year as string.
    /// </summary>
    public string? PublishedYear { get; init; }

    /// <summary>
    /// Book description or synopsis.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Genre list.
    /// </summary>
    public string[]? Genres { get; init; }

    /// <summary>
    /// Tags list.
    /// </summary>
    public string[]? Tags { get; init; }

    /// <summary>
    /// Language code (e.g., "cs", "en").
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// ISBN identifier.
    /// </summary>
    public string? Isbn { get; init; }

    /// <summary>
    /// Amazon ASIN identifier.
    /// </summary>
    public string? Asin { get; init; }

    /// <summary>
    /// Series information array.
    /// </summary>
    public AudiobookshelfSeries[]? Series { get; init; }
}

/// <summary>
/// Represents series information in Audiobookshelf format.
/// </summary>
public record AudiobookshelfSeries
{
    /// <summary>
    /// Series name.
    /// </summary>
    public required string Series { get; init; }

    /// <summary>
    /// Position/sequence number in the series (as string, e.g., "1", "2.5").
    /// </summary>
    public string? Sequence { get; init; }
}
