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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consolidated book metadata with confidence score.</returns>
    Task<BookMetadata> ExtractMetadataAsync(
        AudiobookFolder audiobookFolder,
        CancellationToken cancellationToken = default);
}
