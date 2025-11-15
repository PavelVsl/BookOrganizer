using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Service for extracting metadata from filenames and folder paths.
/// </summary>
public interface IFilenameParser
{
    /// <summary>
    /// Extracts metadata from a folder path.
    /// </summary>
    /// <param name="folderPath">The folder path to parse.</param>
    /// <returns>Extracted book metadata with confidence score.</returns>
    BookMetadata ParseFolderPath(string folderPath);
}
