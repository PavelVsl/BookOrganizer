using BookOrganizer.Models;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// In-memory cache for duplicate resolution decisions.
/// </summary>
public class InMemoryDeduplicationCache : IDeduplicationCache
{
    private readonly ITextNormalizer _textNormalizer;
    private readonly ILogger<InMemoryDeduplicationCache> _logger;
    private readonly ConcurrentDictionary<string, DuplicationResolution> _cache = new();

    public InMemoryDeduplicationCache(
        ITextNormalizer textNormalizer,
        ILogger<InMemoryDeduplicationCache> logger)
    {
        _textNormalizer = textNormalizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<DuplicationResolution?> GetCachedResolutionAsync(
        DuplicationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var key = GenerateCacheKey(candidate);

        if (_cache.TryGetValue(key, out var resolution))
        {
            _logger.LogDebug("Cache hit for key: {Key}", key);
            return Task.FromResult<DuplicationResolution?>(resolution);
        }

        _logger.LogDebug("Cache miss for key: {Key}", key);
        return Task.FromResult<DuplicationResolution?>(null);
    }

    /// <inheritdoc />
    public Task SaveResolutionAsync(
        DuplicationCandidate candidate,
        DuplicationResolution resolution,
        CancellationToken cancellationToken = default)
    {
        var key = GenerateCacheKey(candidate);
        _cache[key] = resolution;

        _logger.LogDebug("Cached resolution for key: {Key} = {Resolution}", key, resolution);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        _cache.Clear();
        _logger.LogInformation("Deduplication cache cleared");
        return Task.CompletedTask;
    }

    private string GenerateCacheKey(DuplicationCandidate candidate)
    {
        // Generate a stable key based on normalized metadata
        // This ensures "František" and "Frantisek" map to the same key
        var author1 = _textNormalizer.NormalizeForComparison(candidate.SourceMetadata.Author);
        var title1 = _textNormalizer.NormalizeForComparison(candidate.SourceMetadata.Title);
        var author2 = _textNormalizer.NormalizeForComparison(candidate.TargetMetadata.Author);
        var title2 = _textNormalizer.NormalizeForComparison(candidate.TargetMetadata.Title);

        // Sort to ensure same key regardless of source/target order
        var parts = new[] { author1, title1, author2, title2 }.OrderBy(p => p).ToArray();

        return string.Join("|", parts);
    }
}
