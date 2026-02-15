using BookOrganizer.Models;

namespace BookOrganizer.Services.Preview;

/// <summary>
/// Service for rendering preview results to the console with rich formatting.
/// </summary>
public interface IPreviewRenderer
{
    /// <summary>
    /// Renders a preview result to the console with tree view and formatting.
    /// </summary>
    /// <param name="preview">Preview result to render.</param>
    /// <param name="options">Rendering options.</param>
    void RenderPreview(PreviewResult preview, PreviewRenderOptions? options = null);

    /// <summary>
    /// Renders only the statistics summary.
    /// </summary>
    /// <param name="statistics">Statistics to render.</param>
    void RenderStatistics(PreviewStatistics statistics);

    /// <summary>
    /// Renders only the issues found.
    /// </summary>
    /// <param name="issues">Issues to render.</param>
    /// <param name="groupBySeverity">Whether to group issues by severity.</param>
    void RenderIssues(IReadOnlyList<PreviewIssue> issues, bool groupBySeverity = true);
}

/// <summary>
/// Options for controlling preview rendering.
/// </summary>
public record PreviewRenderOptions
{
    /// <summary>
    /// Whether to show the full tree view of operations.
    /// </summary>
    public bool ShowTree { get; init; } = true;

    /// <summary>
    /// Whether to show statistics summary.
    /// </summary>
    public bool ShowStatistics { get; init; } = true;

    /// <summary>
    /// Whether to show issues.
    /// </summary>
    public bool ShowIssues { get; init; } = true;

    /// <summary>
    /// Maximum number of operations to show in tree view.
    /// </summary>
    public int? MaxOperationsToShow { get; init; }

    /// <summary>
    /// Whether to show full paths or just filenames.
    /// </summary>
    public bool ShowFullPaths { get; init; } = false;

    /// <summary>
    /// Whether to use compact mode (less spacing and details).
    /// </summary>
    public bool CompactMode { get; init; } = false;
}
