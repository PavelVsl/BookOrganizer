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
        string bookFolderPath,
        string libraryRootPath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metadataFilePath = Path.Combine(bookFolderPath, "metadata.json");

            // Check if metadata.json already exists
            if (File.Exists(metadataFilePath) && !force)
            {
                _logger.LogDebug("Skipping {Path} - metadata.json already exists (use --force to overwrite)", bookFolderPath);
                return new MetadataGenerationResult
                {
                    Success = true,
                    Skipped = true,
                    FilePath = metadataFilePath
                };
            }

            // Parse folder structure
            var normalizedBookPath = Path.GetFullPath(bookFolderPath);
            var normalizedLibraryRoot = Path.GetFullPath(libraryRootPath);

            // Ensure book folder is within library root
            if (!normalizedBookPath.StartsWith(normalizedLibraryRoot, StringComparison.OrdinalIgnoreCase))
            {
                return new MetadataGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Book folder is not within library root: {bookFolderPath}"
                };
            }

            // Get relative path and split into components
            var relativePath = Path.GetRelativePath(normalizedLibraryRoot, normalizedBookPath);
            var pathComponents = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Where(p => !string.IsNullOrWhiteSpace(p) && p != ".")
                .ToArray();

            if (pathComponents.Length < 2)
            {
                var warning = $"Invalid folder structure: {relativePath} (expected Author/Title or Author/Series/Title)";
                _logger.LogWarning(warning);
                return new MetadataGenerationResult
                {
                    Success = false,
                    ErrorMessage = warning
                };
            }

            // Parse structure based on depth
            MetadataOverride metadata;

            if (pathComponents.Length == 2)
            {
                // Structure: library/Author/Title/
                var author = pathComponents[0];
                var title = pathComponents[1];

                metadata = new MetadataOverride
                {
                    Author = author,
                    Title = title,
                    Source = "FolderStructure"
                };

                _logger.LogInformation("Parsed metadata from 2-level structure: {Author} / {Title}", author, title);
            }
            else if (pathComponents.Length >= 3)
            {
                // Structure: library/Author/Series/Title/
                var author = pathComponents[0];
                var series = pathComponents[1];
                var title = pathComponents[2];

                metadata = new MetadataOverride
                {
                    Author = author,
                    Series = series,
                    Title = title,
                    Source = "FolderStructure"
                };

                _logger.LogInformation("Parsed metadata from 3-level structure: {Author} / {Series} / {Title}",
                    author, series, title);
            }
            else
            {
                return new MetadataGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"Unexpected folder structure: {relativePath}"
                };
            }

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
            _logger.LogError(ex, "Failed to generate metadata for {Path}", bookFolderPath);
            return new MetadataGenerationResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
