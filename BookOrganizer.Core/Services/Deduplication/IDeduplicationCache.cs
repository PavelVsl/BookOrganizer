using BookOrganizer.Models;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Caches user decisions for duplicate resolution to avoid repeated prompts.
/// </summary>
public interface IDeduplicationCache
{
    /// <summary>
    /// Gets a cached resolution decision if available.
    /// </summary>
    /// <param name="candidate">Duplication candidate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Cached resolution if found, null otherwise</returns>
    Task<DuplicationResolution?> GetCachedResolutionAsync(
        DuplicationCandidate candidate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a resolution decision to the cache.
    /// </summary>
    /// <param name="candidate">Duplication candidate</param>
    /// <param name="resolution">Chosen resolution</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveResolutionAsync(
        DuplicationCandidate candidate,
        DuplicationResolution resolution,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all cached resolutions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ClearCacheAsync(CancellationToken cancellationToken = default);
}
