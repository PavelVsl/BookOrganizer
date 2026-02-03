using System.Text.Encodings.Web;
using System.Text.Json;
using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Formats metadata in Audiobookshelf JSON format.
/// Compatible with Audiobookshelf server's metadata.json expectations.
/// </summary>
public class AudiobookshelfFormatter : IMetadataFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <inheritdoc />
    public string FileName => "metadata.json";

    /// <inheritdoc />
    public string FormatName => "Audiobookshelf";

    /// <inheritdoc />
    public Task<string> FormatAsync(BookMetadata metadata, CancellationToken cancellationToken = default)
    {
        // Parse genres from semicolon-separated string
        string[]? genres = null;
        if (!string.IsNullOrWhiteSpace(metadata.Genre))
        {
            genres = metadata.Genre
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
        }

        // Build series array
        AudiobookshelfSeries[]? series = null;
        if (!string.IsNullOrWhiteSpace(metadata.Series))
        {
            series =
            [
                new AudiobookshelfSeries
                {
                    Series = metadata.Series,
                    Sequence = metadata.SeriesNumber
                }
            ];
        }

        var audiobookshelfMetadata = new AudiobookshelfMetadata
        {
            Title = metadata.Title,
            Author = metadata.Author,
            Narrator = metadata.Narrator,
            Publisher = null, // Not available in BookMetadata
            PublishedYear = metadata.Year?.ToString(),
            Description = metadata.Description,
            Genres = genres,
            Tags = null, // Not available in BookMetadata
            Language = null, // Not available in BookMetadata
            Isbn = null, // Not available in BookMetadata
            Asin = null, // Not available in BookMetadata
            Series = series
        };

        var json = JsonSerializer.Serialize(audiobookshelfMetadata, JsonOptions);
        return Task.FromResult(json);
    }
}
