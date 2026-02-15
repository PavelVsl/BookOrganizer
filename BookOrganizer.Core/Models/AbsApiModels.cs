using System.Text.Json.Serialization;

namespace BookOrganizer.Models;

/// <summary>
/// Response from GET /api/libraries/{id}/items.
/// </summary>
public record AbsLibraryItemsResponse
{
    [JsonPropertyName("results")]
    public List<AbsLibraryItem> Results { get; init; } = [];

    [JsonPropertyName("total")]
    public int Total { get; init; }
}

/// <summary>
/// A single library item from Audiobookshelf.
/// </summary>
public record AbsLibraryItem
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("media")]
    public AbsMedia? Media { get; init; }
}

/// <summary>
/// Media section of a library item.
/// </summary>
public record AbsMedia
{
    [JsonPropertyName("metadata")]
    public AbsMediaMetadata? Metadata { get; init; }
}

/// <summary>
/// Metadata within a library item's media.
/// </summary>
public record AbsMediaMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("authorName")]
    public string? AuthorName { get; init; }

    [JsonPropertyName("seriesName")]
    public string? SeriesName { get; init; }
}

/// <summary>
/// Response from GET /api/libraries.
/// </summary>
public record AbsLibrariesResponse
{
    [JsonPropertyName("libraries")]
    public List<AbsLibrary> Libraries { get; init; } = [];
}

/// <summary>
/// A single library from Audiobookshelf.
/// </summary>
public record AbsLibrary
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("mediaType")]
    public string MediaType { get; init; } = "";
}

/// <summary>
/// Represents a match between a source audiobook and an existing ABS library item.
/// </summary>
public record AbsDuplicateMatch
{
    /// <summary>
    /// Source audiobook folder path.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Title from source metadata.
    /// </summary>
    public required string SourceTitle { get; init; }

    /// <summary>
    /// Author from source metadata.
    /// </summary>
    public string? SourceAuthor { get; init; }

    /// <summary>
    /// Matching ABS library item ID.
    /// </summary>
    public required string AbsItemId { get; init; }

    /// <summary>
    /// Title in ABS.
    /// </summary>
    public string? AbsTitle { get; init; }

    /// <summary>
    /// Author in ABS.
    /// </summary>
    public string? AbsAuthor { get; init; }

    /// <summary>
    /// Whether title matched.
    /// </summary>
    public bool TitleMatch { get; init; }

    /// <summary>
    /// Whether author matched.
    /// </summary>
    public bool AuthorMatch { get; init; }
}

/// <summary>
/// Actions to take on source folders that are duplicates in ABS.
/// </summary>
public enum AbsDuplicateAction
{
    /// <summary>
    /// Log and skip — don't organize, leave source untouched.
    /// </summary>
    Skip,

    /// <summary>
    /// Prefix source folder with _DUP_ so BO scanner skips it.
    /// </summary>
    Rename,

    /// <summary>
    /// Move source to _duplicates/ subfolder under source root.
    /// </summary>
    Move,

    /// <summary>
    /// Delete source folder (requires --yes confirmation).
    /// </summary>
    Delete
}
