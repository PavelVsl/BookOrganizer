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

    /// <summary>
    /// Saves metadata to bookinfo.json in the specified folder.
    /// Merges with existing file (only non-null fields are overwritten).
    /// Always sets source to "manual" to protect from auto-overwrite.
    /// </summary>
    /// <param name="folderPath">Path to the folder where bookinfo.json will be written.</param>
    /// <param name="metadata">Metadata fields to save (null fields are preserved from existing file).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveMetadataAsync(
        string folderPath,
        MetadataOverride metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the author name in all bookinfo.json files under the library root that match the old author.
    /// </summary>
    /// <param name="libraryRoot">Root path of the library to scan.</param>
    /// <param name="oldAuthor">Current author name to find.</param>
    /// <param name="newAuthor">New author name to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of bookinfo.json files updated.</returns>
    Task<int> BatchUpdateAuthorAsync(
        string libraryRoot,
        string oldAuthor,
        string newAuthor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the series name in all bookinfo.json files under the library root that match the old series.
    /// </summary>
    /// <param name="libraryRoot">Root path of the library to scan.</param>
    /// <param name="oldSeries">Current series name to find.</param>
    /// <param name="newSeries">New series name to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of bookinfo.json files updated.</returns>
    Task<int> BatchUpdateSeriesAsync(
        string libraryRoot,
        string oldSeries,
        string newSeries,
        CancellationToken cancellationToken = default);
}
