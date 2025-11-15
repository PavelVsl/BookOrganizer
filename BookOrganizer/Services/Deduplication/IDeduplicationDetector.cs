using BookOrganizer.Models;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Represents an audiobook with its folder and consolidated metadata.
/// </summary>
public record AudiobookWithMetadata(AudiobookFolder Folder, ConsolidatedMetadata Metadata);

/// <summary>
/// Detects potential duplicate audiobooks using metadata and content analysis.
/// </summary>
public interface IDeduplicationDetector
{
    /// <summary>
    /// Detects potential duplicates among a collection of audiobooks with metadata.
    /// </summary>
    /// <param name="audiobooks">Audiobooks with metadata to analyze</param>
    /// <param name="confidenceThreshold">Minimum confidence score to report (0.0-1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of duplication candidates</returns>
    Task<List<DuplicationCandidate>> DetectDuplicatesAsync(
        IEnumerable<AudiobookWithMetadata> audiobooks,
        double confidenceThreshold = 0.7,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two specific audiobooks for duplication.
    /// </summary>
    /// <param name="audiobook1">First audiobook with metadata</param>
    /// <param name="audiobook2">Second audiobook with metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Duplication candidate if they match, null otherwise</returns>
    Task<DuplicationCandidate?> CompareAudiobooksAsync(
        AudiobookWithMetadata audiobook1,
        AudiobookWithMetadata audiobook2,
        CancellationToken cancellationToken = default);
}
