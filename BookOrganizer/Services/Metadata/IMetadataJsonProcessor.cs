using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Service for discovering and processing hierarchical metadata.json files.
/// Walks up the directory tree from an audiobook folder to the source root,
/// loading and merging metadata.json files at each level.
/// </summary>
public interface IMetadataJsonProcessor
{
    /// <summary>
    /// Loads hierarchical metadata for an audiobook folder by scanning parent directories.
    /// </summary>
    /// <param name="audiobookFolderPath">Path to the audiobook folder containing MP3 files.</param>
    /// <param name="sourceRootPath">Path to the source root (stops scanning here).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Hierarchical metadata with parent chain, or null if no metadata.json found.</returns>
    Task<HierarchicalMetadata?> LoadHierarchicalMetadataAsync(
        string audiobookFolderPath,
        string sourceRootPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a single metadata.json file from a specific folder.
    /// </summary>
    /// <param name="folderPath">Path to folder containing metadata.json.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Metadata override if file exists and is valid, otherwise null.</returns>
    Task<MetadataOverride?> LoadMetadataJsonAsync(
        string folderPath,
        CancellationToken cancellationToken = default);
}
