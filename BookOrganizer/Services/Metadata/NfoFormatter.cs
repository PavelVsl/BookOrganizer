using System.Text;
using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Formats metadata in Audiobookshelf NFO format (key:value pairs).
/// Based on Audiobookshelf's parseNfoMetadata.js parser.
/// </summary>
public class NfoFormatter : IMetadataFormatter
{
    /// <inheritdoc />
    public string FileName => "metadata.nfo";

    /// <inheritdoc />
    public string FormatName => "NFO";

    /// <inheritdoc />
    public Task<string> FormatAsync(BookMetadata metadata, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // Title
        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            sb.AppendLine($"title: {metadata.Title}");
        }

        // Author (semicolon-separated in BookMetadata, comma-separated in NFO)
        if (!string.IsNullOrWhiteSpace(metadata.Author))
        {
            var authors = ConvertSemicolonToComma(metadata.Author);
            sb.AppendLine($"author: {authors}");
        }

        // Narrator (semicolon-separated in BookMetadata, comma-separated in NFO)
        if (!string.IsNullOrWhiteSpace(metadata.Narrator))
        {
            var narrators = ConvertSemicolonToComma(metadata.Narrator);
            sb.AppendLine($"narrator: {narrators}");
        }

        // Series
        if (!string.IsNullOrWhiteSpace(metadata.Series))
        {
            sb.AppendLine($"series name: {metadata.Series}");

            if (!string.IsNullOrWhiteSpace(metadata.SeriesNumber))
            {
                sb.AppendLine($"position in series: {metadata.SeriesNumber}");
            }
        }

        // Genre (semicolon-separated in BookMetadata, comma-separated in NFO)
        if (!string.IsNullOrWhiteSpace(metadata.Genre))
        {
            var genres = ConvertSemicolonToComma(metadata.Genre);
            sb.AppendLine($"genre: {genres}");
        }

        // Year
        if (metadata.Year.HasValue)
        {
            sb.AppendLine($"copyright: {metadata.Year.Value}");
        }

        // Description (multi-line section)
        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            sb.AppendLine();
            sb.AppendLine("book description");
            sb.AppendLine("================================");
            sb.AppendLine(metadata.Description.Trim());
            sb.AppendLine("================================");
        }

        return Task.FromResult(sb.ToString());
    }

    /// <summary>
    /// Converts semicolon-separated values to comma-separated.
    /// </summary>
    private static string ConvertSemicolonToComma(string value)
    {
        return string.Join(", ", value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
