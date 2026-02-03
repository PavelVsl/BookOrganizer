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

        // Walk up from audiobook folder to source root, building hierarchy
        HierarchicalMetadata? currentMetadata = null;
        var currentPath = normalizedAudiobookPath;
        var level = pathComponents.Count - 1; // Start at deepest level (book)

        while (currentPath.StartsWith(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(currentPath, normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            var metadata = await LoadMetadataJsonAsync(currentPath, cancellationToken);

            if (metadata != null || currentMetadata != null)
            {
                // Create hierarchical metadata node
                var hierarchicalNode = new HierarchicalMetadata
                {
                    FolderPath = currentPath,
                    Level = Math.Min(level, 2), // Cap at book level (2)
                    Metadata = metadata,
                    Parent = currentMetadata
                };

                currentMetadata = hierarchicalNode;

                _logger.LogInformation(
                    "Found metadata at level {Level} ({LevelName}) in {Path}",
                    hierarchicalNode.Level,
                    GetLevelName(hierarchicalNode.Level),
                    Path.GetFileName(currentPath));
            }

            // Move up one directory
            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parentPath))
                break;

            currentPath = parentPath;
            level--;
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

    private static string GetLevelName(int level) => level switch
    {
        0 => "Author",
        1 => "Series",
        2 => "Book",
        _ => "Unknown"
    };
}
