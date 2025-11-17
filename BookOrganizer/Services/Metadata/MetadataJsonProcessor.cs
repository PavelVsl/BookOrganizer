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
        var metadataFilePath = Path.Combine(folderPath, "metadata.json");

        if (!File.Exists(metadataFilePath))
        {
            return null;
        }

        try
        {
            await using var fileStream = File.OpenRead(metadataFilePath);
            var metadata = await JsonSerializer.DeserializeAsync<MetadataOverride>(
                fileStream,
                JsonOptions,
                cancellationToken);

            if (metadata == null)
            {
                _logger.LogWarning("metadata.json at {Path} deserialized to null", metadataFilePath);
                return null;
            }

            _logger.LogInformation("Loaded metadata.json from {Path}", folderPath);
            return metadata;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse metadata.json at {Path}: {Message}",
                metadataFilePath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load metadata.json from {Path}", metadataFilePath);
            return null;
        }
    }

    private static string GetLevelName(int level) => level switch
    {
        0 => "Author",
        1 => "Series",
        2 => "Book",
        _ => "Unknown"
    };
}
