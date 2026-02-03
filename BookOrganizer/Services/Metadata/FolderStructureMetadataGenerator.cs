using System.Text.Json;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Generates metadata files from library folder structure.
/// Expects structure: library/Author/[Series/]Title/
/// </summary>
public class FolderStructureMetadataGenerator : IMetadataGenerator
{
    private readonly ILogger<FolderStructureMetadataGenerator> _logger;
    private readonly IEnumerable<IMetadataFormatter> _formatters;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FolderStructureMetadataGenerator(
        ILogger<FolderStructureMetadataGenerator> logger,
        IEnumerable<IMetadataFormatter> formatters)
    {
        _logger = logger;
        _formatters = formatters;
    }

    /// <inheritdoc />
    public Task<MetadataGenerationResult> GenerateMetadataFromStructureAsync(
        string bookFolderPath,
        string libraryRootPath,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        // Default to BookOrganizer format for backward compatibility
        return GenerateMetadataFromStructureAsync(bookFolderPath, libraryRootPath, MetadataFormat.BookOrganizer, force, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MetadataGenerationResult> GenerateMetadataFromStructureAsync(
        string folderPath,
        string libraryRootPath,
        MetadataFormat format,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse folder structure first to get metadata
            var metadataResult = ParseFolderStructure(folderPath, libraryRootPath);
            if (!metadataResult.Success)
            {
                return metadataResult;
            }

            var metadata = metadataResult.Metadata!;

            // Convert to BookMetadata for formatters
            var bookMetadata = new BookMetadata
            {
                Title = metadata.Title ?? Path.GetFileName(folderPath),
                Author = metadata.Author,
                Series = metadata.Series,
                SeriesNumber = metadata.SeriesNumber,
                Year = metadata.Year,
                Genre = metadata.Genre,
                Description = metadata.Description,
                Source = metadata.Source ?? "FolderStructure",
                Confidence = 0.8
            };

            // Get formatters for the specified format
            var formattersToUse = GetFormattersForFormat(format);
            if (!formattersToUse.Any())
            {
                return new MetadataGenerationResult
                {
                    Success = false,
                    ErrorMessage = $"No formatters available for format: {format}"
                };
            }

            var allSkipped = true;
            var generatedFiles = new List<string>();

            foreach (var formatter in formattersToUse)
            {
                var metadataFilePath = Path.Combine(folderPath, formatter.FileName);

                // Check if file already exists
                if (File.Exists(metadataFilePath) && !force)
                {
                    _logger.LogDebug("Skipping {Path} - {FileName} already exists (use --force to overwrite)",
                        folderPath, formatter.FileName);
                    continue;
                }

                allSkipped = false;

                // Format and write
                var content = await formatter.FormatAsync(bookMetadata, cancellationToken);
                await File.WriteAllTextAsync(metadataFilePath, content, cancellationToken);

                generatedFiles.Add(metadataFilePath);
                _logger.LogInformation("Generated {FileName} at {Path}", formatter.FileName, metadataFilePath);
            }

            if (allSkipped)
            {
                return new MetadataGenerationResult
                {
                    Success = true,
                    Skipped = true,
                    Metadata = metadata,
                    FilePath = Path.Combine(folderPath, formattersToUse.First().FileName)
                };
            }

            return new MetadataGenerationResult
            {
                Success = true,
                Metadata = metadata,
                FilePath = generatedFiles.FirstOrDefault(),
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

    /// <summary>
    /// Parses folder structure to extract metadata.
    /// </summary>
    private MetadataGenerationResult ParseFolderStructure(string folderPath, string libraryRootPath)
    {
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

        _logger.LogInformation("Parsed metadata for {Level}-level folder: {Path}",
            pathComponents.Length, relativePath);

        return new MetadataGenerationResult
        {
            Success = true,
            Metadata = metadata
        };
    }

    /// <summary>
    /// Gets formatters for the specified format.
    /// </summary>
    private IEnumerable<IMetadataFormatter> GetFormattersForFormat(MetadataFormat format)
    {
        return format switch
        {
            MetadataFormat.BookOrganizer => _formatters.Where(f => f is BookOrganizerFormatter),
            MetadataFormat.Audiobookshelf => _formatters.Where(f => f is AudiobookshelfFormatter),
            MetadataFormat.Nfo => _formatters.Where(f => f is NfoFormatter),
            MetadataFormat.All => _formatters,
            _ => []
        };
    }
}
