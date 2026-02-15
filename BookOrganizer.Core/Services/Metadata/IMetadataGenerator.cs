using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Service for generating metadata files from folder structure.
/// </summary>
public interface IMetadataGenerator
{
    /// <summary>
    /// Generates metadata file from folder structure.
    /// </summary>
    /// <param name="bookFolderPath">Path to the book folder in library.</param>
    /// <param name="libraryRootPath">Root path of the library.</param>
    /// <param name="force">If true, overwrites existing metadata files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success, the generated metadata, and any error message.</returns>
    Task<MetadataGenerationResult> GenerateMetadataFromStructureAsync(
        string bookFolderPath,
        string libraryRootPath,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates metadata file from folder structure with specified format.
    /// </summary>
    /// <param name="bookFolderPath">Path to the book folder in library.</param>
    /// <param name="libraryRootPath">Root path of the library.</param>
    /// <param name="format">Metadata format to generate.</param>
    /// <param name="force">If true, overwrites existing metadata files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success, the generated metadata, and any error message.</returns>
    Task<MetadataGenerationResult> GenerateMetadataFromStructureAsync(
        string bookFolderPath,
        string libraryRootPath,
        MetadataFormat format,
        bool force = false,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of metadata generation operation.
/// </summary>
public record MetadataGenerationResult
{
    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The generated metadata (null if failed).
    /// </summary>
    public MetadataOverride? Metadata { get; init; }

    /// <summary>
    /// Error message if operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Indicates whether the file was skipped (already exists and force=false).
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Path to the generated metadata.json file.
    /// </summary>
    public string? FilePath { get; init; }
}
