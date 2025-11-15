using BookOrganizer.Models;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Handles user interaction for resolving duplicate audiobooks.
/// </summary>
public interface IDeduplicationResolver
{
    /// <summary>
    /// Presents duplicate candidates to the user and gets resolution decisions.
    /// </summary>
    /// <param name="candidates">Duplication candidates to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping candidate to chosen resolution</returns>
    Task<Dictionary<DuplicationCandidate, DuplicationResolution>> ResolveAsync(
        List<DuplicationCandidate> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Presents a single duplicate candidate to the user and gets resolution decision.
    /// </summary>
    /// <param name="candidate">Duplication candidate to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Chosen resolution</returns>
    Task<DuplicationResolution> ResolveSingleAsync(
        DuplicationCandidate candidate,
        CancellationToken cancellationToken = default);
}
