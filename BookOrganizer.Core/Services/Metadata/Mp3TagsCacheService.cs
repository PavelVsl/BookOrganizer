using System.Text.Encodings.Web;
using System.Text.Json;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Reads and writes mp3tags.json cache files for avoiding repeated TagLib extraction.
/// </summary>
public class Mp3TagsCacheService
{
    private readonly ILogger<Mp3TagsCacheService> _logger;

    public const string CacheFileName = "mp3tags.json";
    private const string CacheVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Mp3TagsCacheService(ILogger<Mp3TagsCacheService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Tries to load a cache file from the given folder.
    /// Returns null if no cache exists or it's unreadable.
    /// </summary>
    public async Task<Mp3TagsCache?> LoadCacheAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(folderPath, CacheFileName);
        if (!File.Exists(cachePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            var cache = JsonSerializer.Deserialize<Mp3TagsCache>(json, JsonOptions);

            if (cache == null || cache.Version != CacheVersion)
            {
                _logger.LogDebug("Cache version mismatch or null in {Path}, ignoring", cachePath);
                return null;
            }

            _logger.LogDebug("Loaded tag cache with {Count} files from {Path}", cache.Files.Count, cachePath);
            return cache;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read tag cache from {Path}, will re-extract", cachePath);
            return null;
        }
    }

    /// <summary>
    /// Writes a cache file to the given folder.
    /// </summary>
    public async Task SaveCacheAsync(string folderPath, Mp3TagsCache cache, CancellationToken cancellationToken = default)
    {
        var cachePath = Path.Combine(folderPath, CacheFileName);

        try
        {
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            await File.WriteAllTextAsync(cachePath, json, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Saved tag cache with {Count} files to {Path}", cache.Files.Count, cachePath);
        }
        catch (Exception ex)
        {
            // Non-fatal — cache write failure shouldn't block extraction
            _logger.LogWarning(ex, "Failed to write tag cache to {Path}", cachePath);
        }
    }

    /// <summary>
    /// Checks if a cached file entry is still valid (file unchanged).
    /// </summary>
    public static bool IsCacheEntryValid(CachedFileTag entry, string fullFilePath)
    {
        try
        {
            var fileInfo = new FileInfo(fullFilePath);
            if (!fileInfo.Exists)
                return false;

            return entry.LastModifiedUtc == fileInfo.LastWriteTimeUtc &&
                   entry.FileSizeBytes == fileInfo.Length;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a lookup dictionary from cache entries keyed by relative path.
    /// </summary>
    public static Dictionary<string, CachedFileTag> BuildCacheLookup(Mp3TagsCache cache)
    {
        return cache.Files.ToDictionary(
            f => f.RelativePath,
            f => f,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a new cache object for the given folder.
    /// </summary>
    public static Mp3TagsCache CreateCache(string folderPath, List<CachedFileTag> files)
    {
        return new Mp3TagsCache
        {
            Version = CacheVersion,
            ScannedAtUtc = DateTime.UtcNow,
            OriginalFolderPath = folderPath,
            Files = files
        };
    }
}
