namespace BookOrganizer.Models;

/// <summary>
/// Represents metadata at a specific level in the folder hierarchy (author, series, or book level).
/// </summary>
public record HierarchicalMetadata
{
    /// <summary>
    /// The folder path where this metadata was found.
    /// </summary>
    public required string FolderPath { get; init; }

    /// <summary>
    /// The hierarchy level: 0=author, 1=series, 2=book
    /// </summary>
    public required int Level { get; init; }

    /// <summary>
    /// The metadata loaded from metadata.json at this level.
    /// </summary>
    public MetadataOverride? Metadata { get; init; }

    /// <summary>
    /// Reference to the parent level metadata (if any).
    /// </summary>
    public HierarchicalMetadata? Parent { get; init; }

    /// <summary>
    /// Gets the effective metadata by cascading from parent levels.
    /// Author field cascades from author-level (Level=0).
    /// Series and SeriesNumber cascade from series-level (Level=1).
    /// Child values override parent values.
    /// Note: Title and Source are not set here - they come from MP3 metadata or filename parsing.
    /// </summary>
    public MetadataOverride GetEffectiveMetadata()
    {
        var effective = new MetadataOverride();

        // Start with parent's effective metadata (recursive cascading)
        if (Parent != null)
        {
            var parentMetadata = Parent.GetEffectiveMetadata();

            // Copy all fields from parent
            effective = new MetadataOverride
            {
                Title = parentMetadata.Title,
                Author = parentMetadata.Author,
                Narrator = parentMetadata.Narrator,
                Series = parentMetadata.Series,
                SeriesNumber = parentMetadata.SeriesNumber,
                Year = parentMetadata.Year,
                Genre = parentMetadata.Genre,
                Description = parentMetadata.Description,
                Language = parentMetadata.Language
            };
        }

        // Apply current level metadata (overrides parent)
        if (Metadata != null)
        {
            effective = new MetadataOverride
            {
                Title = !string.IsNullOrWhiteSpace(Metadata.Title) ? Metadata.Title : effective.Title,
                Author = !string.IsNullOrWhiteSpace(Metadata.Author) ? Metadata.Author : effective.Author,
                Narrator = !string.IsNullOrWhiteSpace(Metadata.Narrator) ? Metadata.Narrator : effective.Narrator,
                Series = !string.IsNullOrWhiteSpace(Metadata.Series) ? Metadata.Series : effective.Series,
                SeriesNumber = !string.IsNullOrWhiteSpace(Metadata.SeriesNumber) ? Metadata.SeriesNumber : effective.SeriesNumber,
                Year = Metadata.Year ?? effective.Year,
                Genre = !string.IsNullOrWhiteSpace(Metadata.Genre) ? Metadata.Genre : effective.Genre,
                Description = !string.IsNullOrWhiteSpace(Metadata.Description) ? Metadata.Description : effective.Description,
                Language = !string.IsNullOrWhiteSpace(Metadata.Language) ? Metadata.Language : effective.Language
            };
        }

        return effective;
    }
}

/// <summary>
/// Identifies the level in the folder hierarchy.
/// </summary>
public enum MetadataLevel
{
    /// <summary>
    /// Author level - typically 2-3 levels from source root.
    /// Example: /source/King Stephen/
    /// </summary>
    Author = 0,

    /// <summary>
    /// Series level - typically 1 level below author.
    /// Example: /source/King Stephen/Temna vez/
    /// </summary>
    Series = 1,

    /// <summary>
    /// Book level - the folder containing MP3 files.
    /// Example: /source/King Stephen/Temna vez/1 - Pistolník/
    /// </summary>
    Book = 2
}
