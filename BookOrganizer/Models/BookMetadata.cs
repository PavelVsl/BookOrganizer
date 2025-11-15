namespace BookOrganizer.Models;

/// <summary>
/// Represents consolidated metadata for an audiobook.
/// </summary>
public record BookMetadata
{
    /// <summary>
    /// Book title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Book author(s). Multiple authors separated by semicolon.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Series name if the book is part of a series.
    /// </summary>
    public string? Series { get; init; }

    /// <summary>
    /// Book number within the series (e.g., "1", "2.5").
    /// </summary>
    public string? SeriesNumber { get; init; }

    /// <summary>
    /// Narrator(s). Multiple narrators separated by semicolon.
    /// </summary>
    public string? Narrator { get; init; }

    /// <summary>
    /// Publication year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Genre(s). Multiple genres separated by semicolon.
    /// </summary>
    public string? Genre { get; init; }

    /// <summary>
    /// Book description or synopsis.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Confidence score for this metadata (0.0 to 1.0).
    /// Higher values indicate more reliable metadata.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Source of this metadata (e.g., "ID3Tags", "Filename", "Manual").
    /// </summary>
    public required string Source { get; init; }
}
