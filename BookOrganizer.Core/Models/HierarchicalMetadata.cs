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
    /// Child values override parent values UNLESS the parent has source="manual" —
    /// manual parent fields are authoritative and cannot be overridden by non-manual children.
    /// </summary>
    public MetadataOverride GetEffectiveMetadata()
    {
        var effective = new MetadataOverride();
        var parentIsManual = false;

        // Start with parent's effective metadata (recursive cascading)
        if (Parent != null)
        {
            effective = Parent.GetEffectiveMetadata();
            // Check if any ancestor in the chain has source=manual
            parentIsManual = IsManualInChain(Parent);
        }

        // Apply current level metadata (overrides parent)
        if (Metadata != null)
        {
            var currentIsManual = string.Equals(Metadata.Source, "manual", StringComparison.OrdinalIgnoreCase);

            // A child can override a parent field only if:
            // - The parent field is NOT from a manual source, OR
            // - The child itself is also manual
            var canOverride = !parentIsManual || currentIsManual;

            effective = new MetadataOverride
            {
                Title = canOverride && !string.IsNullOrWhiteSpace(Metadata.Title) ? Metadata.Title : effective.Title,
                Author = canOverride && !string.IsNullOrWhiteSpace(Metadata.Author) ? Metadata.Author : effective.Author,
                Narrator = canOverride && !string.IsNullOrWhiteSpace(Metadata.Narrator) ? Metadata.Narrator : effective.Narrator,
                Series = canOverride && !string.IsNullOrWhiteSpace(Metadata.Series) ? Metadata.Series : effective.Series,
                SeriesNumber = canOverride && !string.IsNullOrWhiteSpace(Metadata.SeriesNumber) ? Metadata.SeriesNumber : effective.SeriesNumber,
                Year = canOverride ? (Metadata.Year ?? effective.Year) : effective.Year,
                DiscNumber = canOverride ? (Metadata.DiscNumber ?? effective.DiscNumber) : effective.DiscNumber,
                Genre = canOverride && !string.IsNullOrWhiteSpace(Metadata.Genre) ? Metadata.Genre : effective.Genre,
                Description = canOverride && !string.IsNullOrWhiteSpace(Metadata.Description) ? Metadata.Description : effective.Description,
                Language = canOverride && !string.IsNullOrWhiteSpace(Metadata.Language) ? Metadata.Language : effective.Language
            };
        }

        return effective;
    }

    /// <summary>
    /// Checks if any node in the ancestor chain has source="manual".
    /// </summary>
    private static bool IsManualInChain(HierarchicalMetadata? node)
    {
        while (node != null)
        {
            if (node.Metadata != null &&
                string.Equals(node.Metadata.Source, "manual", StringComparison.OrdinalIgnoreCase))
                return true;
            node = node.Parent;
        }
        return false;
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
