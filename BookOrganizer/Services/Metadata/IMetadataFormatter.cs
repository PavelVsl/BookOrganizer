using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Interface for formatting metadata into different output formats.
/// </summary>
public interface IMetadataFormatter
{
    /// <summary>
    /// Gets the file name (including extension) for the metadata file.
    /// </summary>
    string FileName { get; }

    /// <summary>
    /// Gets the display name of this format.
    /// </summary>
    string FormatName { get; }

    /// <summary>
    /// Formats the given metadata into the output format.
    /// </summary>
    /// <param name="metadata">The source metadata to format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The formatted metadata as a string.</returns>
    Task<string> FormatAsync(BookMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents available metadata export formats.
/// </summary>
public enum MetadataFormat
{
    /// <summary>
    /// BookOrganizer's own format (metadata.json with MetadataOverride structure).
    /// </summary>
    BookOrganizer,

    /// <summary>
    /// Audiobookshelf JSON format (metadata.json with Audiobookshelf structure).
    /// </summary>
    Audiobookshelf,

    /// <summary>
    /// Audiobookshelf NFO format (metadata.nfo with key:value pairs).
    /// </summary>
    Nfo,

    /// <summary>
    /// Export all supported formats.
    /// </summary>
    All
}
