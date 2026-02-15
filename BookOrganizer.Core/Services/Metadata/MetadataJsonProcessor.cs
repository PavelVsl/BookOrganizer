using System.Text.Json;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Implements hierarchical metadata.json processing with cascading from parent folders.
/// </summary>
public class MetadataJsonProcessor : IMetadataJsonProcessor
{
    private readonly ILogger<MetadataJsonProcessor> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public MetadataJsonProcessor(ILogger<MetadataJsonProcessor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if a bookinfo.json file has source set to "manual", indicating it should not be overwritten.
    /// Returns false if the file doesn't exist or cannot be parsed (fail-safe: don't block operations).
    /// </summary>
    public static async Task<bool> IsManuallyEditedAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("source", out var sourceEl) &&
                sourceEl.ValueKind == JsonValueKind.String &&
                string.Equals(sourceEl.GetString(), MetadataOverride.ManualSource, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<HierarchicalMetadata?> LoadHierarchicalMetadataAsync(
        string audiobookFolderPath,
        string sourceRootPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedAudiobookPath = Path.GetFullPath(audiobookFolderPath);
        var normalizedSourceRoot = Path.GetFullPath(sourceRootPath);

        // Ensure audiobook folder is within source root
        if (!normalizedAudiobookPath.StartsWith(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Audiobook folder {AudiobookPath} is not within source root {SourceRoot}",
                audiobookFolderPath, sourceRootPath);
            return null;
        }

        // Build path components from source root to audiobook folder
        var relativePath = Path.GetRelativePath(normalizedSourceRoot, normalizedAudiobookPath);
        var pathComponents = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p) && p != ".")
            .ToList();

        if (pathComponents.Count == 0)
        {
            _logger.LogWarning("No path components found between source and audiobook folder");
            return null;
        }

        // Collect metadata from each folder level (deepest first)
        var nodes = new List<(string Path, int Level, MetadataOverride? Metadata)>();
        var currentPath = normalizedAudiobookPath;
        var level = pathComponents.Count - 1; // Start at deepest level (book)

        while (currentPath.StartsWith(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentPath, normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            var metadata = await LoadMetadataJsonAsync(currentPath, cancellationToken);
            nodes.Add((currentPath, Math.Min(level, 2), metadata));

            // Move up one directory
            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parentPath))
                break;

            currentPath = parentPath;
            level--;
        }

        // Reverse to build hierarchy top-down (shallowest=root, deepest=leaf)
        // so that book-level metadata overrides author-level metadata
        nodes.Reverse();

        HierarchicalMetadata? currentMetadata = null;
        foreach (var (nodePath, nodeLevel, metadata) in nodes)
        {
            if (metadata != null || currentMetadata != null)
            {
                currentMetadata = new HierarchicalMetadata
                {
                    FolderPath = nodePath,
                    Level = nodeLevel,
                    Metadata = metadata,
                    Parent = currentMetadata
                };

                _logger.LogInformation(
                    "Found metadata at level {Level} ({LevelName}) in {Path}",
                    nodeLevel,
                    GetLevelName(nodeLevel),
                    Path.GetFileName(nodePath));
            }
        }

        return currentMetadata;
    }

    public async Task<MetadataOverride?> LoadMetadataJsonAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        // Try bookinfo.json first (BookOrganizer format)
        var bookinfoPath = Path.Combine(folderPath, "bookinfo.json");
        if (File.Exists(bookinfoPath))
        {
            var result = await LoadFromFileAsync(bookinfoPath, cancellationToken);
            if (result != null)
                return result;
        }

        // Fall back to metadata.json (legacy BookOrganizer or Audiobookshelf format)
        var metadataFilePath = Path.Combine(folderPath, "metadata.json");
        if (File.Exists(metadataFilePath))
        {
            return await LoadFromFileAsync(metadataFilePath, cancellationToken);
        }

        return null;
    }

    private async Task<MetadataOverride?> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);

            // Try to parse as JsonDocument first to detect format
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if this is Audiobookshelf format (series is an array)
            if (root.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Array)
            {
                return ParseAudiobookshelfFormat(root, filePath);
            }

            // Otherwise, try standard BookOrganizer format
            var metadata = JsonSerializer.Deserialize<MetadataOverride>(json, JsonOptions);

            if (metadata == null)
            {
                _logger.LogWarning("Metadata file at {Path} deserialized to null", filePath);
                return null;
            }

            _logger.LogInformation("Loaded metadata from {Path}", filePath);
            return metadata;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse metadata at {Path}: {Message}",
                filePath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load metadata from {Path}", filePath);
            return null;
        }
    }

    private MetadataOverride ParseAudiobookshelfFormat(JsonElement root, string filePath)
    {
        string? title = null;
        string? author = null;
        string? narrator = null;
        string? series = null;
        string? seriesNumber = null;
        int? year = null;
        string? genre = null;
        string? publisher = null;
        string? description = null;

        if (root.TryGetProperty("title", out var titleEl))
            title = titleEl.GetString();

        if (root.TryGetProperty("author", out var authorEl))
            author = authorEl.GetString();

        if (root.TryGetProperty("narrator", out var narratorEl))
            narrator = narratorEl.GetString();

        if (root.TryGetProperty("publisher", out var publisherEl))
            publisher = publisherEl.GetString();

        if (root.TryGetProperty("description", out var descEl))
            description = descEl.GetString();

        // Parse publishedYear (string in Audiobookshelf format)
        if (root.TryGetProperty("publishedYear", out var yearEl))
        {
            var yearStr = yearEl.GetString();
            if (int.TryParse(yearStr, out var parsedYear))
                year = parsedYear;
        }

        // Parse genres array into semicolon-separated string
        if (root.TryGetProperty("genres", out var genresEl) && genresEl.ValueKind == JsonValueKind.Array)
        {
            var genres = new List<string>();
            foreach (var g in genresEl.EnumerateArray())
            {
                var genreStr = g.GetString();
                if (!string.IsNullOrWhiteSpace(genreStr))
                    genres.Add(genreStr);
            }
            if (genres.Count > 0)
                genre = string.Join("; ", genres);
        }

        // Parse series array - take first series entry
        if (root.TryGetProperty("series", out var seriesEl) && seriesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in seriesEl.EnumerateArray())
            {
                if (s.TryGetProperty("series", out var seriesNameEl))
                {
                    series = seriesNameEl.GetString();
                }
                if (s.TryGetProperty("sequence", out var seqEl))
                {
                    seriesNumber = seqEl.GetString();
                }
                break; // Take only first series
            }
        }

        _logger.LogInformation("Loaded Audiobookshelf format metadata from {Path}", filePath);

        return new MetadataOverride
        {
            Title = title,
            Author = author,
            Narrator = narrator,
            Series = series,
            SeriesNumber = seriesNumber,
            Year = year,
            Genre = genre,
            Publisher = publisher,
            Description = description,
            Source = "Audiobookshelf"
        };
    }

    public async Task SaveMetadataAsync(
        string folderPath,
        MetadataOverride metadata,
        CancellationToken cancellationToken = default)
    {
        var bookinfoPath = Path.Combine(folderPath, "bookinfo.json");

        // Load existing file to merge, preserving fields not in the new metadata
        var existing = await LoadMetadataJsonAsync(folderPath, cancellationToken);

        var merged = new MetadataOverride
        {
            Title = metadata.Title ?? existing?.Title,
            Author = metadata.Author ?? existing?.Author,
            Narrator = metadata.Narrator ?? existing?.Narrator,
            Series = metadata.Series ?? existing?.Series,
            SeriesNumber = metadata.SeriesNumber ?? existing?.SeriesNumber,
            Year = metadata.Year ?? existing?.Year,
            DiscNumber = metadata.DiscNumber ?? existing?.DiscNumber,
            Genre = metadata.Genre ?? existing?.Genre,
            Publisher = metadata.Publisher ?? existing?.Publisher,
            Description = metadata.Description ?? existing?.Description,
            Language = metadata.Language ?? existing?.Language,
            Comment = metadata.Comment ?? existing?.Comment,
            Notes = metadata.Notes ?? existing?.Notes,
            Source = MetadataOverride.ManualSource
        };

        var writeOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(merged, writeOptions);
        await File.WriteAllTextAsync(bookinfoPath, json, cancellationToken);

        _logger.LogInformation("Saved metadata to {Path}", bookinfoPath);
    }

    public async Task<int> BatchUpdateAuthorAsync(
        string libraryRoot,
        string oldAuthor,
        string newAuthor,
        CancellationToken cancellationToken = default)
    {
        return await BatchUpdateFieldAsync(
            libraryRoot,
            m => string.Equals(m.Author, oldAuthor, StringComparison.OrdinalIgnoreCase),
            m => m with { Author = newAuthor },
            cancellationToken);
    }

    public async Task<int> BatchUpdateSeriesAsync(
        string libraryRoot,
        string oldSeries,
        string newSeries,
        CancellationToken cancellationToken = default)
    {
        return await BatchUpdateFieldAsync(
            libraryRoot,
            m => string.Equals(m.Series, oldSeries, StringComparison.OrdinalIgnoreCase),
            m => m with { Series = newSeries },
            cancellationToken);
    }

    private async Task<int> BatchUpdateFieldAsync(
        string libraryRoot,
        Func<MetadataOverride, bool> predicate,
        Func<MetadataOverride, MetadataOverride> transform,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        var bookinfoFiles = Directory.EnumerateFiles(libraryRoot, "bookinfo.json", SearchOption.AllDirectories);

        foreach (var filePath in bookinfoFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var folderPath = Path.GetDirectoryName(filePath)!;
            var metadata = await LoadMetadataJsonAsync(folderPath, cancellationToken);

            if (metadata != null && predicate(metadata))
            {
                var transformed = transform(metadata);
                await SaveMetadataAsync(folderPath, transformed, cancellationToken);
                updated++;

                _logger.LogInformation("Updated bookinfo.json in {Path}", folderPath);
            }
        }

        _logger.LogInformation("Batch updated {Count} bookinfo.json file(s) in {Root}", updated, libraryRoot);
        return updated;
    }

    private static string GetLevelName(int level) => level switch
    {
        0 => "Author",
        1 => "Series",
        2 => "Book",
        _ => "Unknown"
    };
}
