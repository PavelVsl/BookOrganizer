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

    public MetadataExtractor(
        ILogger<MetadataExtractor> logger,
        IFilenameParser filenameParser,
        IMetadataConsolidator consolidator)
    {
        _logger = logger;
        _filenameParser = filenameParser;
        _consolidator = consolidator;
    }

    /// <inheritdoc />
    public async Task<BookMetadata> ExtractMetadataAsync(
        AudiobookFolder audiobookFolder,
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
        var id3Metadata = ConsolidateMetadata(fileMetadataList);

        // Get metadata from filename/folder structure
        var filenameMetadata = _filenameParser.ParseFolderPath(audiobookFolder.Path);

        // Consolidate metadata from multiple sources
        var metadataSources = new[] { id3Metadata, filenameMetadata };
        var consolidatedResult = await _consolidator.ConsolidateAsync(metadataSources, cancellationToken);
        var consolidated = consolidatedResult.ToBookMetadata();

        _logger.LogInformation(
            "Extracted metadata: Title='{Title}' (from {TitleSource}), Author='{Author}' (from {AuthorSource}), Overall Confidence={Confidence:F2}",
            consolidatedResult.Title,
            consolidatedResult.TitleSource,
            consolidatedResult.Author,
            consolidatedResult.AuthorSource,
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

                return new FileMetadata
                {
                    FilePath = filePath,
                    Title = GetStringValue(tag.Title),
                    Album = GetStringValue(tag.Album),
                    Artist = GetStringValue(tag.FirstPerformer),
                    AlbumArtist = GetStringValue(tag.FirstAlbumArtist),
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

    private BookMetadata ConsolidateMetadata(List<FileMetadata> fileMetadataList)
    {
        // Use the most common values across all files
        var title = GetMostCommonValue(fileMetadataList.Select(m => m.Album).Where(v => v != null));
        var author = GetMostCommonValue(fileMetadataList.Select(m => m.AlbumArtist ?? m.Artist ?? m.Composer).Where(v => v != null));
        var narrator = GetMostCommonValue(fileMetadataList.Select(m => m.Artist).Where(v => v != null));
        var genre = GetMostCommonValue(fileMetadataList.Select(m => m.Genre).Where(v => v != null));

        // Year: use the most common non-zero year
        var years = fileMetadataList.Select(m => m.Year).Where(y => y > 0).ToList();
        var year = years.Count > 0 ? (uint?)GetMostCommonValue(years) : null;

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
