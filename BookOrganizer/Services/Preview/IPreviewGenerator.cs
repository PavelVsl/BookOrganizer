using BookOrganizer.Models;

namespace BookOrganizer.Services.Preview;

/// <summary>
/// Service for generating previews of organization operations without executing them.
/// </summary>
public interface IPreviewGenerator
{
    /// <summary>
    /// Generates a preview of organizing audiobooks from source to destination.
    /// </summary>
    /// <param name="sourcePath">Source directory containing audiobooks.</param>
    /// <param name="destinationPath">Destination directory for organized library.</param>
    /// <param name="operationType">Type of file operation to use.</param>
    /// <param name="filter">Optional filter to limit which audiobooks are included.</param>
    /// <param name="detectDuplicates">Whether to detect duplicate audiobooks.</param>
    /// <param name="duplicateThreshold">Minimum confidence threshold for duplicate detection (0.0-1.0).</param>
    /// <param name="rebuildCache">Force rebuild of library cache by rescanning existing library books.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preview result with operations, statistics, and issues.</returns>
    Task<PreviewResult> GeneratePreviewAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        PreviewFilter? filter = null,
        bool detectDuplicates = false,
        double duplicateThreshold = 0.7,
        bool rebuildCache = false,
        CancellationToken cancellationToken = default,
        OrganizationOptions? options = null);

    /// <summary>
    /// Generates a preview for specific organization plans.
    /// </summary>
    /// <param name="plans">List of organization plans to preview.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Preview result with operations, statistics, and issues.</returns>
    Task<PreviewResult> GeneratePreviewFromPlansAsync(
        IEnumerable<OrganizationPlan> plans,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports preview results to a file in the specified format.
    /// </summary>
    /// <param name="preview">Preview result to export.</param>
    /// <param name="outputPath">Output file path.</param>
    /// <param name="format">Export format (JSON, CSV, or Text).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExportPreviewAsync(
        PreviewResult preview,
        string outputPath,
        ExportFormat format,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter criteria for preview generation.
/// </summary>
public record PreviewFilter
{
    /// <summary>
    /// Filter by specific authors (case-insensitive).
    /// </summary>
    public IReadOnlyList<string>? Authors { get; init; }

    /// <summary>
    /// Filter by specific series (case-insensitive).
    /// </summary>
    public IReadOnlyList<string>? Series { get; init; }

    /// <summary>
    /// Minimum confidence score (0.0 to 1.0) for metadata.
    /// </summary>
    public double? MinimumConfidence { get; init; }

    /// <summary>
    /// Only include operations with specific issue severities.
    /// </summary>
    public IReadOnlyList<IssueSeverity>? IssueSeverities { get; init; }

    /// <summary>
    /// Maximum number of items to include in preview.
    /// </summary>
    public int? MaxItems { get; init; }
}

/// <summary>
/// Format for exporting preview results.
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// JSON format with full details.
    /// </summary>
    Json,

    /// <summary>
    /// CSV format with tabular data.
    /// </summary>
    Csv,

    /// <summary>
    /// Plain text format with human-readable output.
    /// </summary>
    Text
}
