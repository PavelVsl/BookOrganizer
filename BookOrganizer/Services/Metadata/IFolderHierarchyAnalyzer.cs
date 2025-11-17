using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Analyzes folder hierarchy to detect author and series from parent folder structure.
/// </summary>
public interface IFolderHierarchyAnalyzer
{
    /// <summary>
    /// Analyzes the folder hierarchy to extract potential author and series information.
    /// Walks up from the audiobook folder to detect patterns like /Author/Series/Book.
    /// </summary>
    /// <param name="audiobookFolderPath">Path to the audiobook folder containing MP3 files.</param>
    /// <param name="sourceRootPath">Path to the source root (stops analysis here).</param>
    /// <returns>Metadata extracted from folder structure, or null if no patterns detected.</returns>
    FolderHierarchyMetadata? AnalyzeHierarchy(string audiobookFolderPath, string sourceRootPath);
}

/// <summary>
/// Metadata extracted from folder hierarchy analysis.
/// </summary>
public record FolderHierarchyMetadata
{
    /// <summary>
    /// Detected author name from parent folder structure.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Detected series name from parent folder structure.
    /// </summary>
    public string? Series { get; init; }

    /// <summary>
    /// Confidence level of the detection (0.0 to 1.0).
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// The folder level where author was detected (0=source root, 1=first level, etc.).
    /// </summary>
    public int? AuthorLevel { get; init; }

    /// <summary>
    /// The folder level where series was detected.
    /// </summary>
    public int? SeriesLevel { get; init; }
}
