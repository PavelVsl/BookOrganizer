using System.Net.Http.Headers;
using System.Text.Json;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// HttpClient-based implementation of the Audiobookshelf API client.
/// </summary>
public class AbsApiClient : IAbsApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AbsApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AbsApiClient(string baseUrl, string apiToken, ILogger<AbsApiClient> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/'))
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiToken);
    }

    /// <inheritdoc />
    public async Task<List<AbsLibrary>> GetLibrariesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Fetching libraries from ABS");
        var response = await _httpClient.GetAsync("/api/libraries", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<AbsLibrariesResponse>(json, JsonOptions);
        var libraries = result?.Libraries ?? [];

        _logger.LogInformation("Found {Count} libraries in ABS", libraries.Count);
        return libraries;
    }

    /// <inheritdoc />
    public async Task<List<AbsLibraryItem>> GetLibraryItemsAsync(
        string libraryId,
        CancellationToken cancellationToken = default)
    {
        // Use limit=0 to get all items in a single request
        _logger.LogDebug("Fetching all items from ABS library {LibraryId}", libraryId);
        var response = await _httpClient.GetAsync(
            $"/api/libraries/{libraryId}/items?limit=0",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<AbsLibraryItemsResponse>(json, JsonOptions);
        var items = result?.Results ?? [];

        _logger.LogInformation("Found {Count} items in ABS library {LibraryId}", items.Count, libraryId);
        return items;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
