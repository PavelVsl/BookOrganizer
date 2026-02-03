using System.Text.Encodings.Web;
using System.Text.Json;
using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Formats metadata in BookOrganizer's own format (MetadataOverride structure).
/// </summary>
public class BookOrganizerFormatter : IMetadataFormatter
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
    public string FormatName => "BookOrganizer";

    /// <inheritdoc />
    public Task<string> FormatAsync(BookMetadata metadata, CancellationToken cancellationToken = default)
    {
        var metadataOverride = new MetadataOverride
        {
            Title = metadata.Title,
            Author = metadata.Author,
            Narrator = metadata.Narrator,
            Series = metadata.Series,
            SeriesNumber = metadata.SeriesNumber,
            Year = metadata.Year,
            Genre = metadata.Genre,
            Description = metadata.Description,
            Source = metadata.Source
        };

        var json = JsonSerializer.Serialize(metadataOverride, JsonOptions);
        return Task.FromResult(json);
    }
}
