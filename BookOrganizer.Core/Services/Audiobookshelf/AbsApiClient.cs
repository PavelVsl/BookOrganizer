using System.Net.Http.Headers;
using System.Text.Json;
using BookOrganizer.Models;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// HttpClient-based implementation of the Audiobookshelf API client.
/// Supports lazy configuration — can be registered as singleton and configured later.
/// </summary>
public class AbsApiClient : IAbsApiClient, IDisposable
{
    private HttpClient? _httpClient;
    private readonly ILogger<AbsApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public bool IsConfigured => _httpClient != null;

    public AbsApiClient(ILogger<AbsApiClient> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Configure(string baseUrl, string apiKey)
    {
        _httpClient?.Dispose();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/'))
        };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        _logger.LogInformation("ABS client configured for {BaseUrl}", baseUrl);
    }

    private HttpClient GetClient()
    {
        return _httpClient ?? throw new InvalidOperationException(
            "ABS client is not configured. Call Configure() with server URL and API key first.");
    }

    /// <inheritdoc />
    public async Task<List<AbsLibrary>> GetLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        _logger.LogDebug("Fetching libraries from ABS");
        var response = await client.GetAsync("/api/libraries", cancellationToken).ConfigureAwait(false);
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
        var client = GetClient();
        _logger.LogDebug("Fetching all items from ABS library {LibraryId}", libraryId);
        var response = await client.GetAsync(
            $"/api/libraries/{libraryId}/items?limit=0",
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<AbsLibraryItemsResponse>(json, JsonOptions);
        var items = result?.Results ?? [];

        _logger.LogInformation("Found {Count} items in ABS library {LibraryId}", items.Count, libraryId);
        return items;
    }

    /// <inheritdoc />
    public async Task ScanLibraryAsync(string libraryId, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        _logger.LogInformation("Triggering scan for ABS library {LibraryId}", libraryId);
        var response = await client.PostAsync(
            $"/api/libraries/{libraryId}/scan",
            null,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        _logger.LogInformation("Library scan triggered for {LibraryId}", libraryId);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
