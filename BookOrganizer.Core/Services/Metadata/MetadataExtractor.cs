using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private readonly Mp3TagsCacheService _tagsCacheService;

    public MetadataExtractor(
        ILogger<MetadataExtractor> logger,
        IFilenameParser filenameParser,
        IMetadataConsolidator consolidator,
        IMetadataJsonProcessor metadataJsonProcessor,
        IFolderHierarchyAnalyzer folderHierarchyAnalyzer,
        Mp3TagsCacheService tagsCacheService)
    {
        _logger = logger;
        _filenameParser = filenameParser;
        _consolidator = consolidator;
        _metadataJsonProcessor = metadataJsonProcessor;
        _folderHierarchyAnalyzer = folderHierarchyAnalyzer;
        _tagsCacheService = tagsCacheService;
    }

    /// <inheritdoc />
    public Task<BookMetadata> ExtractMetadataAsync(
        AudiobookFolder audiobookFolder,
        string? sourceRootPath = null,
        CancellationToken cancellationToken = default)
    {
        return ExtractMetadataInternalAsync(audiobookFolder, sourceRootPath, cacheOnly: false, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BookMetadata> ExtractMetadataCachedOnlyAsync(
        AudiobookFolder audiobookFolder,
        string? sourceRootPath = null,
        CancellationToken cancellationToken = default)
    {
        return ExtractMetadataInternalAsync(audiobookFolder, sourceRootPath, cacheOnly: true, cancellationToken);
    }

    private async Task<BookMetadata> ExtractMetadataInternalAsync(
        AudiobookFolder audiobookFolder,
        string? sourceRootPath,
        bool cacheOnly,
        CancellationToken cancellationToken)
    {
        if (audiobookFolder.AudioFiles.Count == 0)
        {
            throw new MetadataExtractionException(
                "No audio files found in audiobook folder",
                audiobookFolder.Path);
        }

        _logger.LogInformation(
            "Extracting metadata from {Count} files in {Path} (cacheOnly={CacheOnly})",
            audiobookFolder.AudioFiles.Count,
            audiobookFolder.Path,
            cacheOnly);

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

        // Extract metadata from all files (with tag cache support)
        var fileMetadataList = cacheOnly
            ? await ExtractCachedFileMetadataAsync(audiobookFolder, cancellationToken).ConfigureAwait(false)
            : await ExtractAllFileMetadataAsync(audiobookFolder, cancellationToken).ConfigureAwait(false);

        // Get metadata from ID3 tags (may be empty if cacheOnly and no cache exists)
        BookMetadata? id3Metadata = null;
        if (fileMetadataList.Count > 0)
        {
            id3Metadata = ConsolidateMetadata(fileMetadataList, audiobookFolder.Path);
        }
        else if (!cacheOnly)
        {
            throw new MetadataExtractionException(
                "Failed to extract metadata from any audio files",
                audiobookFolder.Path);
        }

        // Get metadata from filename/folder structure
        // When sourceRootPath is provided, use relative path to avoid parent folders
        // outside the library root polluting the filename parser
        var pathForParsing = !string.IsNullOrWhiteSpace(sourceRootPath)
            ? Path.GetRelativePath(sourceRootPath, audiobookFolder.Path)
            : audiobookFolder.Path;
        var filenameMetadata = _filenameParser.ParseFolderPath(pathForParsing);

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
                DiscNumber = effectiveMetadata.DiscNumber,
                Genre = effectiveMetadata.Genre,
                Description = effectiveMetadata.Description,
                Language = effectiveMetadata.Language,
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

        // Carry Comment from ID3 tags (not part of consolidation)
        if (id3Metadata != null && !string.IsNullOrWhiteSpace(id3Metadata.Comment))
        {
            consolidated = consolidated with { Comment = id3Metadata.Comment };
        }

        // Apply metadata overrides only from manually-edited files (source=manual)
        // Auto-generated files should not override fresh extraction (circular dependency)
        if (overrideMetadata != null && hierarchicalMetadata == null &&
            string.Equals(overrideMetadata.Source, MetadataOverride.ManualSource, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Applying manual metadata overrides from {Path}", audiobookFolder.Path);
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

    /// <summary>
    /// Loads metadata from mp3tags.json cache only. Returns empty list if no cache exists.
    /// </summary>
    private async Task<List<FileMetadata>> ExtractCachedFileMetadataAsync(
        AudiobookFolder audiobookFolder,
        CancellationToken cancellationToken)
    {
        var folderPath = audiobookFolder.Path;
        var cache = await _tagsCacheService.LoadCacheAsync(folderPath, cancellationToken).ConfigureAwait(false);

        if (cache == null || cache.Files.Count == 0)
        {
            _logger.LogDebug("No mp3tags.json cache for {Path}, skipping MP3 tag extraction", folderPath);
            return [];
        }

        var result = new List<FileMetadata>(cache.Files.Count);
        foreach (var entry in cache.Files)
        {
            var tags = entry.Tags;
            result.Add(new FileMetadata
            {
                FilePath = Path.Combine(folderPath, entry.RelativePath),
                Title = tags.Title,
                Album = tags.Album,
                Artist = tags.Artist,
                AlbumArtist = tags.AlbumArtist,
                Composer = tags.Composer,
                Genre = tags.Genre,
                Year = tags.Year,
                Comment = tags.Comment,
                Duration = TimeSpan.FromSeconds(tags.DurationSeconds),
                Bitrate = tags.Bitrate
            });
        }

        _logger.LogInformation("Loaded {Count} entries from mp3tags.json cache for {Path}",
            result.Count, Path.GetFileName(folderPath));
        return result;
    }

    /// <summary>
    /// Extracts metadata from all audio files, using mp3tags.json cache when available.
    /// </summary>
    private async Task<List<FileMetadata>> ExtractAllFileMetadataAsync(
        AudiobookFolder audiobookFolder,
        CancellationToken cancellationToken)
    {
        var folderPath = audiobookFolder.Path;
        var fileMetadataList = new List<FileMetadata>();

        // Try to load existing cache
        var cache = await _tagsCacheService.LoadCacheAsync(folderPath, cancellationToken).ConfigureAwait(false);
        var cacheLookup = cache != null
            ? Mp3TagsCacheService.BuildCacheLookup(cache)
            : new Dictionary<string, CachedFileTag>();

        var cacheHits = 0;
        var cacheMisses = 0;
        var newCacheEntries = new List<CachedFileTag>();

        foreach (var audioFile in audiobookFolder.AudioFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(folderPath, audioFile);

            // Check cache first
            if (cacheLookup.TryGetValue(relativePath, out var cachedEntry) &&
                Mp3TagsCacheService.IsCacheEntryValid(cachedEntry, audioFile))
            {
                // Use cached data
                var tags = cachedEntry.Tags;
                fileMetadataList.Add(new FileMetadata
                {
                    FilePath = audioFile,
                    Title = tags.Title,
                    Album = tags.Album,
                    Artist = tags.Artist,
                    AlbumArtist = tags.AlbumArtist,
                    Composer = tags.Composer,
                    Genre = tags.Genre,
                    Year = tags.Year,
                    Comment = tags.Comment,
                    Duration = TimeSpan.FromSeconds(tags.DurationSeconds),
                    Bitrate = tags.Bitrate
                });
                newCacheEntries.Add(cachedEntry);
                cacheHits++;
                continue;
            }

            // Extract fresh from file
            var fileMetadata = await ExtractFileMetadataAsync(audioFile).ConfigureAwait(false);
            if (fileMetadata != null)
            {
                fileMetadataList.Add(fileMetadata);

                // Build cache entry for this file
                var fileInfo = new FileInfo(audioFile);
                newCacheEntries.Add(new CachedFileTag
                {
                    RelativePath = relativePath,
                    LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                    FileSizeBytes = fileInfo.Length,
                    Tags = new CachedTagData
                    {
                        Title = fileMetadata.Title,
                        Album = fileMetadata.Album,
                        Artist = fileMetadata.Artist,
                        AlbumArtist = fileMetadata.AlbumArtist,
                        Composer = fileMetadata.Composer,
                        Genre = fileMetadata.Genre,
                        Year = fileMetadata.Year,
                        Comment = fileMetadata.Comment,
                        DurationSeconds = fileMetadata.Duration.TotalSeconds,
                        Bitrate = fileMetadata.Bitrate
                    }
                });
                cacheMisses++;
            }
        }

        // Write updated cache if we had any misses (new or changed files)
        if (cacheMisses > 0)
        {
            var newCache = Mp3TagsCacheService.CreateCache(folderPath, newCacheEntries);
            await _tagsCacheService.SaveCacheAsync(folderPath, newCache, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Tag cache for {Path}: {Hits} hits, {Misses} fresh extractions",
                Path.GetFileName(folderPath), cacheHits, cacheMisses);
        }
        else if (cacheHits > 0)
        {
            _logger.LogInformation(
                "Tag cache hit for {Path}: all {Count} files from cache",
                Path.GetFileName(folderPath), cacheHits);
        }

        return fileMetadataList;
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
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return FixCzechEncoding(trimmed);
    }

    /// <summary>
    /// Converts ALL CAPS text to title case (e.g., "ANDRZEJ SAPKOWSKI" → "Andrzej Sapkowski").
    /// Returns original value if not all caps.
    /// </summary>
    private static string? FixAllCaps(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var letters = value.Where(char.IsLetter).ToList();
        if (letters.Count <= 1)
            return value;

        var upperCount = letters.Count(char.IsUpper);
        if ((double)upperCount / letters.Count < 0.7)
            return value;

        var culture = CultureInfo.CurrentCulture;
        return culture.TextInfo.ToTitleCase(value.ToLower(culture));
    }

    /// <summary>
    /// Detects and fixes Windows-1250 Czech text that was incorrectly read as Latin-1 (ISO 8859-1).
    /// Old Czech MP3 files often have ID3 tags written in Windows-1250 but TagLib reads them as Latin-1,
    /// producing garbled text (e.g., 'è' instead of 'č', 'ø' instead of 'ř').
    /// </summary>
    private static string FixCzechEncoding(string text)
    {
        // If text already contains Czech-specific diacritics, it's correctly encoded
        if (ContainsCzechSpecificChars(text))
            return text;

        // Check if text contains characters that suggest Windows-1250 misread as Latin-1
        if (!LooksLikeMisencodedCzech(text))
            return text;

        try
        {
            // Re-encode: treat the string as Latin-1 bytes, then decode as Windows-1250
            var latin1 = Encoding.GetEncoding("iso-8859-1");
            var win1250 = Encoding.GetEncoding("windows-1250");

            var bytes = latin1.GetBytes(text);
            var reencoded = win1250.GetString(bytes);

            // Only use the result if it actually contains Czech characters
            if (ContainsCzechSpecificChars(reencoded))
                return reencoded;
        }
        catch
        {
            // If conversion fails, return original
        }

        return text;
    }

    /// <summary>
    /// Checks if text contains Czech-specific diacritical characters
    /// that differ between Windows-1250 and Latin-1 encodings.
    /// Characters like á, é, í are the same in both encodings so they don't help detect misencoding.
    /// </summary>
    private static bool ContainsCzechSpecificChars(string text)
    {
        foreach (var c in text)
        {
            if (c is 'č' or 'ď' or 'ě' or 'ň' or 'ř' or 'š' or 'ť' or 'ů' or 'ž'
                or 'Č' or 'Ď' or 'Ě' or 'Ň' or 'Ř' or 'Š' or 'Ť' or 'Ů' or 'Ž')
                return true;
        }
        return false;
    }

    /// <summary>
    /// Detects if text looks like Windows-1250 Czech text misread as Latin-1.
    /// When Windows-1250 bytes are interpreted as Latin-1:
    ///   č(0xE8)→è, ě(0xEC)→ì, ď(0xEF)→ï, ň(0xF2)→ò, ř(0xF8)→ø, ů(0xF9)→ù
    ///   š(0x9A)→C1 control, ť(0x9D)→C1 control, ž(0x9E)→C1 control
    /// </summary>
    private static bool LooksLikeMisencodedCzech(string text)
    {
        foreach (var c in text)
        {
            // Latin-1 chars that are actually Czech Windows-1250 chars
            if (c is 'è' or 'ì' or 'ï' or 'ò' or 'ø' or 'ù'        // č, ě, ď, ň, ř, ů
                or 'È' or 'Ì' or 'Ï' or 'Ò' or 'Ø' or 'Ù'          // Č, Ě, Ď, Ň, Ř, Ů
                or '\u008A' or '\u008E'                                // Š, Ž (C1 control area)
                or '\u009A' or '\u009D' or '\u009E')                   // š, ť, ž (C1 control area)
                return true;
        }
        return false;
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
        string? language = null;

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

        if (root.TryGetProperty("language", out var langEl))
            language = langEl.GetString();

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
            Language = language,
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
            DiscNumber = overrides.DiscNumber ?? metadata.DiscNumber,
            Genre = overrides.Genre ?? metadata.Genre,
            Description = overrides.Description ?? metadata.Description,
            Language = overrides.Language ?? metadata.Language,
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

        // Determine author and narrator from ID3 tags
        // When Composer is present, it's the book author and Artist is the narrator
        var composer = GetMostCommonValue(fileMetadataList.Select(m => m.Composer).Where(v => v != null));
        var rawArtist = GetMostCommonValue(fileMetadataList.Select(m => m.Artist).Where(v => v != null));
        if (string.IsNullOrWhiteSpace(rawArtist))
        {
            rawArtist = GetMostCommonValue(fileMetadataList.Select(m => m.AlbumArtist).Where(v => v != null));
        }

        _logger.LogDebug("Raw artist field: {RawArtist}, Composer: {Composer}", rawArtist, composer);

        string? author;
        string? narrator;

        if (!string.IsNullOrWhiteSpace(composer))
        {
            // Composer = author, Artist = narrator
            author = composer;
            narrator = rawArtist;
        }
        else
        {
            // Parse Czech audiobook format: "Author / cte Narrator" or "Author / ucinkuji Narrators"
            (author, narrator) = ParseCzechAuthorNarrator(rawArtist);
        }

        // Get raw comment value
        var comment = GetMostCommonValue(fileMetadataList.Select(m => m.Comment).Where(v => v != null));

        // If narrator not found yet, try Comment field (e.g., "Čte: Martin Stránský")
        if (string.IsNullOrWhiteSpace(narrator))
        {
            narrator = ParseNarratorFromComment(comment);
        }

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
        (var series, var seriesNumber, var cleanTitle) = ExtractSeriesInfo(title);
        if (cleanTitle != null)
            title = cleanTitle;

        // Fix ALL CAPS values from ID3 tags
        title = FixAllCaps(title);
        author = FixAllCaps(author);
        series = FixAllCaps(series);
        narrator = FixAllCaps(narrator);

        // Calculate confidence score
        var confidence = CalculateConfidence(title, author, narrator, genre, year);

        return new BookMetadata
        {
            Title = title ?? "Unknown Title",
            Author = author,
            Series = series,
            SeriesNumber = seriesNumber,
            Narrator = narrator,
            Comment = comment,
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

    /// <summary>
    /// Extracts series info from a title like "Legie 5 - Aga", "LEGIE VII: Mrtvá schránka",
    /// or "Legie - Operace Petragun". Returns (series, seriesNumber, cleanTitle).
    /// Series number can be arabic (5) or roman (VII).
    /// </summary>
    private static (string? series, string? seriesNumber, string? cleanTitle) ExtractSeriesInfo(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (null, null, null);

        // Pattern 1: "{Series} {Number} {separator} {Title}" with arabic or roman numbers
        // e.g., "LEGIE VII: Mrtvá schránka", "Legie 5 - Aga", "Zaklínač 2 - Meč osudu"
        var matchWithNumber = Regex.Match(title,
            @"^(.+?)\s+(\d+|[IVXLCDM]+)\s*[:\-–—]\s*(.+)$",
            RegexOptions.IgnoreCase);

        if (matchWithNumber.Success)
        {
            var numberStr = matchWithNumber.Groups[2].Value.Trim();

            // Validate it's actually a number (arabic or roman)
            if (int.TryParse(numberStr, out _) || TryParseRomanNumeral(numberStr, out _))
            {
                var seriesPart = matchWithNumber.Groups[1].Value.Trim();
                var titlePart = matchWithNumber.Groups[3].Value.Trim();

                var seriesNumber = TryParseRomanNumeral(numberStr, out var arabic)
                    ? arabic.ToString()
                    : numberStr;

                return (seriesPart, seriesNumber, titlePart);
            }
        }

        // Pattern 2: "{Series} {separator} {Title}" without number (single-word series name)
        // e.g., "Legie - Operace Petragun"
        // Requires whitespace before separator to avoid splitting compound words like "SCI-FI"
        var matchNoNumber = Regex.Match(title, @"^(\S+)\s+[:\-–—]\s*(.+)$");
        if (matchNoNumber.Success)
        {
            var seriesPart = matchNoNumber.Groups[1].Value.Trim();
            var titlePart = matchNoNumber.Groups[2].Value.Trim();

            if (!string.IsNullOrWhiteSpace(seriesPart) && !string.IsNullOrWhiteSpace(titlePart))
            {
                return (seriesPart, null, titlePart);
            }
        }

        return (null, null, null);
    }

    /// <summary>
    /// Tries to parse a roman numeral string (e.g., "VII") to an integer.
    /// </summary>
    private static bool TryParseRomanNumeral(string input, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var upper = input.ToUpperInvariant();

        var romanValues = new Dictionary<char, int>
        {
            ['I'] = 1, ['V'] = 5, ['X'] = 10,
            ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000
        };

        var total = 0;
        for (int i = 0; i < upper.Length; i++)
        {
            if (!romanValues.TryGetValue(upper[i], out var current))
                return false;

            var next = (i + 1 < upper.Length && romanValues.TryGetValue(upper[i + 1], out var n)) ? n : 0;

            if (current < next)
                total -= current;
            else
                total += current;
        }

        if (total <= 0)
            return false;

        result = total;
        return true;
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
            var keywordIndex = artistField.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase);

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

    /// <summary>
    /// Parses narrator name from the Comment field.
    /// Supports formats like:
    /// - "Čte: Martin Stránský"
    /// - "Cte: Martin Stránský"
    /// - "čte Martin Stránský"
    /// - "Účinkují: Narrator1, Narrator2"
    /// </summary>
    private static string? ParseNarratorFromComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        string[] narratorKeywords = ["čte:", "cte:", "čte", "cte", "čtou:", "ctou:", "čtou", "ctou",
            "účinkují:", "ucinkuji:", "účinkuje:", "ucinkuje:", "účinkují", "ucinkuji", "účinkuje", "ucinkuje"];

        foreach (var keyword in narratorKeywords)
        {
            var index = comment.IndexOf(keyword, StringComparison.CurrentCultureIgnoreCase);
            if (index >= 0)
            {
                var narrator = comment[(index + keyword.Length)..].Trim();
                narrator = narrator.TrimEnd('.', ',', ';');

                if (!string.IsNullOrWhiteSpace(narrator))
                    return narrator;
            }
        }

        return null;
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
