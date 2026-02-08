using BookOrganizer.Models;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// Client for Audiobookshelf REST API.
/// </summary>
public interface IAbsApiClient
{
    /// <summary>
    /// Gets all libraries from the ABS server.
    /// </summary>
    Task<List<AbsLibrary>> GetLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all items in a library.
    /// </summary>
    Task<List<AbsLibraryItem>> GetLibraryItemsAsync(string libraryId, CancellationToken cancellationToken = default);
}
