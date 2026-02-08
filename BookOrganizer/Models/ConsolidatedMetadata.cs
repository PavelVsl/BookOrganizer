namespace BookOrganizer.Models;

/// <summary>
/// Represents consolidated metadata from multiple sources with per-field confidence scores.
/// </summary>
public record ConsolidatedMetadata
{
    /// <summary>
    /// Book title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Confidence score for the title field (0.0 to 1.0).
    /// </summary>
    public double TitleConfidence { get; init; }

    /// <summary>
    /// Source that provided the title value.
    /// </summary>
    public string? TitleSource { get; init; }

    /// <summary>
    /// Book author(s). Multiple authors separated by semicolon.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Confidence score for the author field (0.0 to 1.0).
    /// </summary>
    public double AuthorConfidence { get; init; }

    /// <summary>
    /// Source that provided the author value.
    /// </summary>
    public string? AuthorSource { get; init; }

    /// <summary>
    /// Series name if the book is part of a series.
    /// </summary>
    public string? Series { get; init; }

    /// <summary>
    /// Confidence score for the series field (0.0 to 1.0).
    /// </summary>
    public double SeriesConfidence { get; init; }

    /// <summary>
    /// Source that provided the series value.
    /// </summary>
    public string? SeriesSource { get; init; }

    /// <summary>
    /// Book number within the series (e.g., "1", "2.5").
    /// </summary>
    public string? SeriesNumber { get; init; }

    /// <summary>
    /// Confidence score for the series number field (0.0 to 1.0).
    /// </summary>
    public double SeriesNumberConfidence { get; init; }

    /// <summary>
    /// Source that provided the series number value.
    /// </summary>
    public string? SeriesNumberSource { get; init; }

    /// <summary>
    /// Narrator(s). Multiple narrators separated by semicolon.
    /// </summary>
    public string? Narrator { get; init; }

    /// <summary>
    /// Confidence score for the narrator field (0.0 to 1.0).
    /// </summary>
    public double NarratorConfidence { get; init; }

    /// <summary>
    /// Source that provided the narrator value.
    /// </summary>
    public string? NarratorSource { get; init; }

    /// <summary>
    /// Publication year.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Confidence score for the year field (0.0 to 1.0).
    /// </summary>
    public double YearConfidence { get; init; }

    /// <summary>
    /// Source that provided the year value.
    /// </summary>
    public string? YearSource { get; init; }

    /// <summary>
    /// Genre(s). Multiple genres separated by semicolon.
    /// </summary>
    public string? Genre { get; init; }

    /// <summary>
    /// Confidence score for the genre field (0.0 to 1.0).
    /// </summary>
    public double GenreConfidence { get; init; }

    /// <summary>
    /// Source that provided the genre value.
    /// </summary>
    public string? GenreSource { get; init; }

    /// <summary>
    /// Book description or synopsis.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Confidence score for the description field (0.0 to 1.0).
    /// </summary>
    public double DescriptionConfidence { get; init; }

    /// <summary>
    /// Source that provided the description value.
    /// </summary>
    public string? DescriptionSource { get; init; }

    /// <summary>
    /// Language code (e.g., "cs" for Czech, "en" for English).
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Overall confidence score calculated from all field confidences (0.0 to 1.0).
    /// Represents the weighted average of all field confidence scores.
    /// </summary>
    public double OverallConfidence { get; init; }

    /// <summary>
    /// All metadata sources that contributed to this consolidated result.
    /// </summary>
    public IReadOnlyList<string> ContributingSources { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Converts this ConsolidatedMetadata to a simple BookMetadata record.
    /// </summary>
    /// <returns>BookMetadata with overall confidence and primary source.</returns>
    public BookMetadata ToBookMetadata()
    {
        // Determine primary source (source with highest contribution)
        var primarySource = ContributingSources.FirstOrDefault() ?? "Unknown";

        return new BookMetadata
        {
            Title = Title,
            Author = Author,
            Series = Series,
            SeriesNumber = SeriesNumber,
            Narrator = Narrator,
            Year = Year,
            Genre = Genre,
            Description = Description,
            Language = Language,
            Confidence = OverallConfidence,
            Source = primarySource
        };
    }

    /// <summary>
    /// Creates a ConsolidatedMetadata from a simple BookMetadata record.
    /// All field confidences are set to the overall confidence from the source.
    /// </summary>
    /// <param name="metadata">Source metadata to convert.</param>
    /// <returns>ConsolidatedMetadata with uniform confidence scores.</returns>
    public static ConsolidatedMetadata FromBookMetadata(BookMetadata metadata)
    {
        return new ConsolidatedMetadata
        {
            Title = metadata.Title,
            TitleConfidence = metadata.Confidence,
            TitleSource = metadata.Source,

            Author = metadata.Author,
            AuthorConfidence = metadata.Confidence,
            AuthorSource = metadata.Source,

            Series = metadata.Series,
            SeriesConfidence = metadata.Confidence,
            SeriesSource = metadata.Source,

            SeriesNumber = metadata.SeriesNumber,
            SeriesNumberConfidence = metadata.Confidence,
            SeriesNumberSource = metadata.Source,

            Narrator = metadata.Narrator,
            NarratorConfidence = metadata.Confidence,
            NarratorSource = metadata.Source,

            Year = metadata.Year,
            YearConfidence = metadata.Confidence,
            YearSource = metadata.Source,

            Genre = metadata.Genre,
            GenreConfidence = metadata.Confidence,
            GenreSource = metadata.Source,

            Description = metadata.Description,
            DescriptionConfidence = metadata.Confidence,
            DescriptionSource = metadata.Source,

            OverallConfidence = metadata.Confidence,
            ContributingSources = new[] { metadata.Source }
        };
    }
}
