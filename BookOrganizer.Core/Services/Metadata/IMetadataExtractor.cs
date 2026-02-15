using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Service for extracting metadata from audiobook files.
/// </summary>
public interface IMetadataExtractor
{
    /// <summary>
    /// Extracts metadata from an audiobook folder.
    /// Analyzes all audio files and consolidates metadata.
    /// </summary>
    /// <param name="audiobookFolder">The audiobook folder to analyze.</param>
    /// <param name="sourceRootPath">Optional source root path for hierarchical metadata detection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consolidated book metadata with confidence score.</returns>
    Task<BookMetadata> ExtractMetadataAsync(
        AudiobookFolder audiobookFolder,
        string? sourceRootPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts metadata using only cached MP3 tags (mp3tags.json), folder structure and bookinfo.json.
    /// Does NOT read actual MP3 files — fast but less complete if no cache exists.
    /// </summary>
    Task<BookMetadata> ExtractMetadataCachedOnlyAsync(
        AudiobookFolder audiobookFolder,
        string? sourceRootPath = null,
        CancellationToken cancellationToken = default);
}
