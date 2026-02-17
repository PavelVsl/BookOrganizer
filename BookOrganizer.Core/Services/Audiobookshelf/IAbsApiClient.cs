using BookOrganizer.Models;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// Client for Audiobookshelf REST API.
/// </summary>
public interface IAbsApiClient
{
    /// <summary>
    /// Whether the client has been configured with server URL and API key.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Configures the client with server URL and API key.
    /// Can be called multiple times to reconfigure.
    /// </summary>
    void Configure(string baseUrl, string apiKey);

    /// <summary>
    /// Gets all libraries from the ABS server.
    /// </summary>
    Task<List<AbsLibrary>> GetLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all items in a library.
    /// </summary>
    Task<List<AbsLibraryItem>> GetLibraryItemsAsync(string libraryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a library scan on the ABS server.
    /// </summary>
    Task ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default);
}
