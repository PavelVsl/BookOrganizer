namespace BookOrganizer.Models;

/// <summary>
/// Represents metadata override from metadata.json file.
/// All fields are optional - only specified fields will override extracted metadata.
/// </summary>
public record MetadataOverride
{
    /// <summary>
    /// Value for Source field indicating the file was manually edited and should not be overwritten.
    /// </summary>
    public const string ManualSource = "manual";

    /// <summary>
    /// Book title (overrides ID3 tags and filename parsing).
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Book author (overrides ID3 tags and filename parsing).
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Narrator name (overrides ID3 tags).
    /// </summary>
    public string? Narrator { get; init; }

    /// <summary>
    /// Series name (overrides ID3 tags).
    /// </summary>
    public string? Series { get; init; }

    /// <summary>
    /// Series number (overrides ID3 tags).
    /// </summary>
    public string? SeriesNumber { get; init; }

    /// <summary>
    /// Publication year (overrides ID3 tags).
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Disc/volume number for multi-volume audiobooks.
    /// When set, the book is organized into a disc subfolder (e.g., "Disk 1").
    /// </summary>
    public int? DiscNumber { get; init; }

    /// <summary>
    /// Genre (overrides ID3 tags).
    /// </summary>
    public string? Genre { get; init; }

    /// <summary>
    /// Publisher (overrides ID3 tags).
    /// </summary>
    public string? Publisher { get; init; }

    /// <summary>
    /// Description or summary.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Language code (e.g., "cs" for Czech, "en" for English).
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// Raw comment from MP3 tags.
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// Additional notes or comments.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Source of metadata extraction (e.g., "MP3Tags", "FolderStructure").
    /// </summary>
    public string? Source { get; init; }
}
