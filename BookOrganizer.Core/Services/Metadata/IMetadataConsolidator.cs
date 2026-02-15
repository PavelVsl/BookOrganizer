using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Service for consolidating metadata from multiple sources with confidence scoring.
/// </summary>
public interface IMetadataConsolidator
{
    /// <summary>
    /// Consolidates metadata from multiple sources (ID3 tags, filename, folder structure).
    /// </summary>
    /// <param name="metadataSources">Collection of metadata from different sources.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Consolidated metadata with per-field confidence scores.</returns>
    Task<ConsolidatedMetadata> ConsolidateAsync(
        IEnumerable<BookMetadata> metadataSources,
        CancellationToken cancellationToken = default);
}
