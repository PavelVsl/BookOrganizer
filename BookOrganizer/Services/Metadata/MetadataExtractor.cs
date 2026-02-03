using System.Text.Json;
using BookOrganizer.Infrastructure.Exceptions;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;
using TagLib;
using File = TagLib.File;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Extracts metadata from audio files using TagLib-Sharp.
/// </summary>
public class MetadataExtractor : IMetadataExtractor
{
    private readonly ILogger<MetadataExtractor> _logger;
    private readonly IFilenameParser _filenameParser;
    private readonly IMetadataConsolidator _consolidator;
    private readonly IMetadataJsonProcessor _metadataJsonProcessor;
    private readonly IFolderHierarchyAnalyzer _folderHierarchyAnalyzer;

    public MetadataExtractor(
        ILogger<MetadataExtractor> logger,
        IFilenameParser filenameParser,
        IMetadataConsolidator consolidator,
        IMetadataJsonProcessor metadataJsonProcessor,
        IFolderHierarchyAnalyzer folderHierarchyAnalyzer)
    {
        _logger = logger;
        _filenameParser = filenameParser;
        _consolidator = consolidator;
        _metadataJsonProcessor = metadataJsonProcessor;
        _folderHierarchyAnalyzer = folderHierarchyAnalyzer;
    }

    /// <inheritdoc />
    public async Task<BookMetadata> ExtractMetadataAsync(
        AudiobookFolder audiobookFolder,
        string? sourceRootPath = null,
        CancellationToken cancellationToken = default)
    {
        if (audiobookFolder.AudioFiles.Count == 0)
        {
            throw new MetadataExtractionException(
                "No audio files found in audiobook folder",
                audiobookFolder.Path);
        }

        _logger.LogInformation(
            "Extracting metadata from {Count} files in {Path}",
            audiobookFolder.AudioFiles.Count,
            audiobookFolder.Path);

        // Load hierarchical metadata from parent folders (if sourceRoot provided)
        HierarchicalMetadata? hierarchicalMetadata = null;
        if (!string.IsNullOrWhiteSpace(sourceRootPath))
        {
            hierarchicalMetadata = await _metadataJsonProcessor.LoadHierarchicalMetadataAsync(
                audiobookFolder.Path,
                sourceRootPath,
                cancellationToken);
        }

        // Check for metadata override file first (immediate folder)
        // Try bookinfo.json (BookOrganizer format) first, then metadata.json (Audiobookshelf or legacy)
        var overrideMetadata = await LoadMetadataOverrideAsync(audiobookFolder.Path, cancellationToken);

        // Extract metadata from all files
        var fileMetadataList = new List<FileMetadata>();

        foreach (var audioFile in audiobookFolder.AudioFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileMetadata = await ExtractFileMetadataAsync(audioFile);
            if (fileMetadata != null)
            {
                fileMetadataList.Add(fileMetadata);
            }
        }

        if (fileMetadataList.Count == 0)
        {
            throw new MetadataExtractionException(
                "Failed to extract metadata from any audio files",
                audiobookFolder.Path);
        }

        // Get metadata from ID3 tags
        var id3Metadata = ConsolidateMetadata(fileMetadataList, audiobookFolder.Path);

        // Get metadata from filename/folder structure
        var filenameMetadata = _filenameParser.ParseFolderPath(audiobookFolder.Path);

        // Get metadata from folder hierarchy (if sourceRoot provided)
        BookMetadata? folderHierarchyMetadata = null;
        if (!string.IsNullOrWhiteSpace(sourceRootPath))
        {
            var hierarchyInfo = _folderHierarchyAnalyzer.AnalyzeHierarchy(audiobookFolder.Path, sourceRootPath);
            if (hierarchyInfo != null)
            {
                folderHierarchyMetadata = new BookMetadata
                {
                    Title = string.Empty, // Not provided by folder hierarchy
                    Source = "FolderHierarchy",
                    Author = hierarchyInfo.Author,
                    Series = hierarchyInfo.Series,
                    Confidence = hierarchyInfo.Confidence
                };

                _logger.LogDebug(
                    "Folder hierarchy detected: Author={Author}, Series={Series}, Confidence={Confidence:F2}",
                    hierarchyInfo.Author, hierarchyInfo.Series, hierarchyInfo.Confidence);
            }
        }

        // Get hierarchical metadata from metadata.json files (highest priority for Author/Series)
        BookMetadata? hierarchicalJsonMetadata = null;
        if (hierarchicalMetadata != null)
        {
            var effectiveMetadata = hierarchicalMetadata.GetEffectiveMetadata();
            hierarchicalJsonMetadata = new BookMetadata
            {
                Title = effectiveMetadata.Title ?? string.Empty,
                Source = "HierarchicalMetadataJson",
                Author = effectiveMetadata.Author,
                Series = effectiveMetadata.Series,
                SeriesNumber = effectiveMetadata.SeriesNumber,
                Narrator = effectiveMetadata.Narrator,
                Year = effectiveMetadata.Year,
                Genre = effectiveMetadata.Genre,
                Description = effectiveMetadata.Description,
                Confidence = 0.95 // High confidence for explicit metadata.json
            };

            _logger.LogInformation(
                "Hierarchical metadata loaded: Author={Author}, Series={Series}",
                effectiveMetadata.Author, effectiveMetadata.Series);
        }

        // Consolidate metadata from multiple sources
        // Priority: hierarchical metadata.json > folder hierarchy > ID3 > filename
        var metadataSources = new[] { hierarchicalJsonMetadata, folderHierarchyMetadata, id3Metadata, filenameMetadata }
            .Where(m => m != null)
            .Cast<BookMetadata>()
            .ToArray();

        var consolidatedResult = await _consolidator.ConsolidateAsync(metadataSources, cancellationToken);
        var consolidated = consolidatedResult.ToBookMetadata();

        // Apply metadata overrides if present (immediate folder only - already handled by hierarchical)
        if (overrideMetadata != null && hierarchicalMetadata == null)
        {
            _logger.LogInformation("Applying metadata overrides from {Path}", audiobookFolder.Path);
            consolidated = ApplyOverrides(consolidated, overrideMetadata);
        }

        // Final fallback: if title is generic/placeholder, use folder name
        var isGenericTitle = consolidated.Title == "Unknown Title" ||
                            consolidated.Title == "Audiobooks" ||
                            consolidated.Title == "Audiobook";

        if (isGenericTitle)
        {
            var folderName = Path.GetFileName(audiobookFolder.Path);
            _logger.LogInformation("Title '{OldTitle}' is generic, using folder name: {Title}", consolidated.Title, folderName);
            consolidated = consolidated with { Title = folderName };
        }

        _logger.LogInformation(
            "Extracted metadata: Title='{Title}' (from {TitleSource}), Author='{Author}' (from {AuthorSource}), Overall Confidence={Confidence:F2}",
            consolidated.Title,
            overrideMetadata != null && overrideMetadata.Title != null ? "metadata.json" : consolidatedResult.TitleSource,
            consolidated.Author,
            overrideMetadata != null && overrideMetadata.Author != null ? "metadata.json" : consolidatedResult.AuthorSource,
            consolidated.Confidence);

        return consolidated;
    }

    private async Task<FileMetadata?> ExtractFileMetadataAsync(string filePath)
    {
        try
        {
            // Use Task.Run to avoid blocking on file I/O
            return await Task.Run(() =>
            {
                using var file = File.Create(filePath);
                var tag = file.Tag;

                // Join all performers - Czech audiobooks often have "Author / cte Narrator" in performers
                var allPerformers = tag.Performers != null && tag.Performers.Length > 0
                    ? string.Join("; ", tag.Performers)
                    : null;
                var allAlbumArtists = tag.AlbumArtists != null && tag.AlbumArtists.Length > 0
                    ? string.Join("; ", tag.AlbumArtists)
                    : null;

                return new FileMetadata
                {
                    FilePath = filePath,
                    Title = GetStringValue(tag.Title),
                    Album = GetStringValue(tag.Album),
                    Artist = GetStringValue(allPerformers),
                    AlbumArtist = GetStringValue(allAlbumArtists),
                    Composer = GetStringValue(tag.FirstComposer),
                    Genre = GetStringValue(tag.FirstGenre),
                    Year = tag.Year,
                    Comment = GetStringValue(tag.Comment),
                    Duration = file.Properties.Duration,
                    Bitrate = file.Properties.AudioBitrate
                };
            });
        }
        catch (CorruptFileException ex)
        {
            _logger.LogWarning(ex, "Corrupt audio file: {FilePath}", filePath);
            return null;
        }
        catch (UnsupportedFormatException ex)
        {
            _logger.LogWarning(ex, "Unsupported file format: {FilePath}", filePath);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting metadata from: {FilePath}", filePath);
            return null;
        }
    }

    private static string? GetStringValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Loads metadata override from bookinfo.json or metadata.json file if either exists.
    /// Supports both BookOrganizer format (bookinfo.json) and Audiobookshelf format (metadata.json).
    /// </summary>
    private async Task<MetadataOverride?> LoadMetadataOverrideAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        // Try bookinfo.json first (BookOrganizer format)
        var bookinfoPath = Path.Combine(folderPath, "bookinfo.json");
        if (System.IO.File.Exists(bookinfoPath))
        {
            var result = await LoadMetadataFromFileAsync(bookinfoPath, cancellationToken);
            if (result != null)
                return result;
        }

        // Fall back to metadata.json (Audiobookshelf or legacy BookOrganizer format)
        var metadataJsonPath = Path.Combine(folderPath, "metadata.json");
        if (System.IO.File.Exists(metadataJsonPath))
        {
            return await LoadMetadataFromFileAsync(metadataJsonPath, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Loads metadata from a specific JSON file.
    /// Supports both BookOrganizer and Audiobookshelf formats.
    /// </summary>
    private async Task<MetadataOverride?> LoadMetadataFromFileAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            // Try to parse as JsonDocument first to detect format
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check if this is Audiobookshelf format (series is an array)
            if (root.TryGetProperty("series", out var seriesElement) &&
                seriesElement.ValueKind == JsonValueKind.Array)
            {
                return ParseAudiobookshelfFormat(root);
            }

            // Otherwise, try standard BookOrganizer format
            return JsonSerializer.Deserialize<MetadataOverride>(json, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load metadata from {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Parses Audiobookshelf format metadata.json into MetadataOverride.
    /// </summary>
    private MetadataOverride ParseAudiobookshelfFormat(JsonElement root)
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

    /// <summary>
    /// Applies metadata overrides to consolidated metadata.
    /// Only non-null fields in the override will replace existing values.
    /// </summary>
    private static BookMetadata ApplyOverrides(BookMetadata metadata, MetadataOverride overrides)
    {
        return metadata with
        {
            Title = overrides.Title ?? metadata.Title,
            Author = overrides.Author ?? metadata.Author,
            Narrator = overrides.Narrator ?? metadata.Narrator,
            Series = overrides.Series ?? metadata.Series,
            SeriesNumber = overrides.SeriesNumber ?? metadata.SeriesNumber,
            Year = overrides.Year ?? metadata.Year,
            Genre = overrides.Genre ?? metadata.Genre,
            Description = overrides.Description ?? metadata.Description,
            // Set confidence to 1.0 for manual overrides
            Confidence = 1.0,
            Source = "metadata.json"
        };
    }

    private BookMetadata ConsolidateMetadata(List<FileMetadata> fileMetadataList, string folderPath)
    {
        // Use the most common values across all files
        var title = GetMostCommonValue(fileMetadataList.Select(m => m.Album).Where(v => v != null));
        var genre = GetMostCommonValue(fileMetadataList.Select(m => m.Genre).Where(v => v != null));

        // For author/narrator, prefer Artist field as it often contains "Author / cte Narrator" format
        // Fall back to AlbumArtist or Composer if Artist is empty
        var rawArtist = GetMostCommonValue(fileMetadataList.Select(m => m.Artist).Where(v => v != null));
        if (string.IsNullOrWhiteSpace(rawArtist))
        {
            rawArtist = GetMostCommonValue(fileMetadataList.Select(m => m.AlbumArtist ?? m.Composer).Where(v => v != null));
        }

        _logger.LogDebug("Raw artist field: {RawArtist}", rawArtist);

        // Parse Czech audiobook format: "Author / cte Narrator" or "Author / ucinkuji Narrators"
        var (author, narrator) = ParseCzechAuthorNarrator(rawArtist);

        // Year: use the most common non-zero year
        var years = fileMetadataList.Select(m => m.Year).Where(y => y > 0).ToList();
        var year = years.Count > 0 ? (uint?)GetMostCommonValue(years) : null;

        // If title is missing or "Unknown Title", use folder name as fallback
        if (string.IsNullOrWhiteSpace(title) || title == "Unknown Title")
        {
            title = Path.GetFileName(folderPath);
            _logger.LogInformation("Using folder name as title: {Title}", title);
        }

        // Try to extract series information from title/album
        (var series, var seriesNumber) = ExtractSeriesInfo(title);

        // Calculate confidence score
        var confidence = CalculateConfidence(title, author, narrator, genre, year);

        return new BookMetadata
        {
            Title = title ?? "Unknown Title",
            Author = author,
            Series = series,
            SeriesNumber = seriesNumber,
            Narrator = narrator,
            Year = (int?)year,
            Genre = genre,
            Confidence = confidence,
            Source = "ID3Tags"
        };
    }

    private static T? GetMostCommonValue<T>(IEnumerable<T> values)
    {
        var list = values.ToList();
        if (list.Count == 0)
            return default;

        return list.GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;
    }

    private static (string? series, string? seriesNumber) ExtractSeriesInfo(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null);

        // Try to find patterns like "Series Name 01", "Series - 02", "Series Name, Díl 1", etc.
        // This is a simple implementation - can be enhanced with regex in Task 4
        var parts = title.Split(new[] { '-', ':', ',', '/' }, StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
        {
            // Check if last part looks like a number
            var lastPart = parts[^1].Trim();
            if (int.TryParse(lastPart, out _) ||
                lastPart.StartsWith("Díl", StringComparison.OrdinalIgnoreCase) ||
                lastPart.StartsWith("Part", StringComparison.OrdinalIgnoreCase) ||
                lastPart.StartsWith("Book", StringComparison.OrdinalIgnoreCase))
            {
                var series = string.Join(" ", parts[..^1]).Trim();
                var number = lastPart.Split(' ', StringSplitOptions.TrimEntries).LastOrDefault();
                return (series, number);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Parses Czech audiobook format where author and narrator are combined.
    /// Supports formats like:
    /// - "Author / cte Narrator" (cte = reads)
    /// - "Author / čte Narrator" (čte = reads, with diacritics)
    /// - "Author ; cte Narrator" (semicolon from joined array)
    /// - "Author / ucinkuji Narrator1, Narrator2" (ucinkuji = perform)
    /// - "Author / účinkují Narrator1, Narrator2" (účinkují = perform, with diacritics)
    /// </summary>
    private static (string? author, string? narrator) ParseCzechAuthorNarrator(string? artistField)
    {
        if (string.IsNullOrWhiteSpace(artistField))
            return (null, null);

        // Narrator keywords (Czech for "reads", "read by", "perform")
        string[] narratorKeywords = ["cte", "čte", "ctou", "čtou", "ucinkuji", "účinkují", "účinkuje"];

        // Look for separator (/ or ;) followed by narrator keyword
        foreach (var keyword in narratorKeywords)
        {
            // Try to find patterns like "/ cte", "; cte", "/cte", ";cte" with flexible whitespace
            var separatorIndex = -1;
            var keywordIndex = artistField.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);

            if (keywordIndex > 0)
            {
                // Look backwards for separator (/ or ;)
                var beforeKeyword = artistField[..keywordIndex].TrimEnd();
                if (beforeKeyword.EndsWith('/') || beforeKeyword.EndsWith(';'))
                {
                    separatorIndex = beforeKeyword.Length - 1;

                    var author = artistField[..separatorIndex].Trim();
                    var narrator = artistField[(keywordIndex + keyword.Length)..].Trim();

                    // Clean up narrator - remove trailing punctuation
                    narrator = narrator.TrimEnd('.', ',', ';');

                    if (!string.IsNullOrWhiteSpace(author) && !string.IsNullOrWhiteSpace(narrator))
                    {
                        return (author, narrator);
                    }
                }
            }
        }

        // No narrator pattern found - the whole string is likely just the author
        return (artistField, null);
    }

    private static double CalculateConfidence(
        string? title,
        string? author,
        string? narrator,
        string? genre,
        uint? year)
    {
        var score = 0.0;

        // Title is most important
        if (!string.IsNullOrWhiteSpace(title) && title != "Unknown Title")
            score += 0.4;

        // Author/Artist
        if (!string.IsNullOrWhiteSpace(author))
            score += 0.3;

        // Narrator
        if (!string.IsNullOrWhiteSpace(narrator))
            score += 0.1;

        // Genre
        if (!string.IsNullOrWhiteSpace(genre))
            score += 0.1;

        // Year (with reasonable range validation)
        if (year.HasValue && year.Value >= 1900 && year.Value <= DateTime.UtcNow.Year + 1)
            score += 0.1;

        return score;
    }

    /// <summary>
    /// Represents metadata extracted from a single audio file.
    /// </summary>
    private record FileMetadata
    {
        public required string FilePath { get; init; }
        public string? Title { get; init; }
        public string? Album { get; init; }
        public string? Artist { get; init; }
        public string? AlbumArtist { get; init; }
        public string? Composer { get; init; }
        public string? Genre { get; init; }
        public uint Year { get; init; }
        public string? Comment { get; init; }
        public TimeSpan Duration { get; init; }
        public int Bitrate { get; init; }
    }
}
