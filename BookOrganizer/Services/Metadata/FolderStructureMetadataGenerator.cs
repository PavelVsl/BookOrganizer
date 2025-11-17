using System.Text.Json;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Generates metadata.json files from library folder structure.
/// Expects structure: library/Author/[Series/]Title/
/// </summary>
public class FolderStructureMetadataGenerator : IMetadataGenerator
{
    private readonly ILogger<FolderStructureMetadataGenerator> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FolderStructureMetadataGenerator(ILogger<FolderStructureMetadataGenerator> logger)
    {
        _logger = logger;
    }

    public async Task<MetadataGenerationResult> GenerateMetadataFromStructureAsync(
        string folderPath,
        string libraryRootPath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metadataFilePath = Path.Combine(folderPath, "metadata.json");

            // Check if metadata.json already exists
            if (File.Exists(metadataFilePath) && !force)
            {
                _logger.LogDebug("Skipping {Path} - metadata.json already exists (use --force to overwrite)", folderPath);
                return new MetadataGenerationResult
                {
                    Success = true,
                    Skipped = true,
                    FilePath = metadataFilePath
                };
            }

            // Parse folder structure
            var normalizedFolderPath = Path.GetFullPath(folderPath);
            var normalizedLibraryRoot = Path.GetFullPath(libraryRootPath);

            // Ensure folder is within library root
            if (!normalizedFolderPath.StartsWith(normalizedLibraryRoot, StringComparison.OrdinalIgnoreCase))
            {
                return new MetadataGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Folder is not within library root: {folderPath}"
                };
            }

            // Get relative path and split into components
            var relativePath = Path.GetRelativePath(normalizedLibraryRoot, normalizedFolderPath);
            var pathComponents = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(p => !string.IsNullOrWhiteSpace(p) && p != ".")
                .ToArray();

            if (pathComponents.Length < 1)
            {
                var warning = $"Invalid folder structure: {relativePath}";
                _logger.LogWarning(warning);
                return new MetadataGenerationResult
                {
                    Success = false,
                    ErrorMessage = warning
                };
            }

            // Determine if this is a book folder (contains audio files) or intermediate folder
            var hasAudioFiles = Directory.GetFiles(folderPath, "*.mp3", SearchOption.TopDirectoryOnly).Any() ||
                                Directory.GetFiles(folderPath, "*.m4a", SearchOption.TopDirectoryOnly).Any() ||
                                Directory.GetFiles(folderPath, "*.m4b", SearchOption.TopDirectoryOnly).Any();

            // Parse structure based on depth
            MetadataOverride metadata = pathComponents.Length switch
            {
                // Author level: library/Author/
                1 => new MetadataOverride
                {
                    Author = pathComponents[0],
                    Source = "FolderStructure"
                },

                // Level 2: Could be Series or Title (depends on if it contains audio files)
                2 when hasAudioFiles => new MetadataOverride
                {
                    Author = pathComponents[0],
                    Title = pathComponents[1],
                    Source = "FolderStructure"
                },

                // Level 2: Series folder (no audio files, will have book subfolders)
                2 => new MetadataOverride
                {
                    Author = pathComponents[0],
                    Series = pathComponents[1],
                    Source = "FolderStructure"
                },

                // Book level: library/Author/Series/Title/
                >= 3 => new MetadataOverride
                {
                    Author = pathComponents[0],
                    Series = pathComponents[1],
                    Title = pathComponents[2],
                    Source = "FolderStructure"
                },

                // Should never reach here due to earlier validation
                _ => throw new InvalidOperationException($"Unexpected path depth: {pathComponents.Length}")
            };

            _logger.LogInformation("Generated metadata for {Level}-level folder: {Path}",
                pathComponents.Length, relativePath);

            // Write metadata.json
            await using var fileStream = File.Create(metadataFilePath);
            await JsonSerializer.SerializeAsync(fileStream, metadata, JsonOptions, cancellationToken);

            _logger.LogInformation("Generated metadata.json at {Path}", metadataFilePath);

            return new MetadataGenerationResult
            {
                Success = true,
                Metadata = metadata,
                FilePath = metadataFilePath,
                Skipped = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate metadata for {Path}", folderPath);
            return new MetadataGenerationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
