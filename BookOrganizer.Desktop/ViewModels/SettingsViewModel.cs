using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Services.Audiobookshelf;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IAbsApiClient _absApiClient;
    private readonly AppSettings _settings;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty] private string _absServerUrl = "";
    [ObservableProperty] private string _absApiKey = "";
    [ObservableProperty] private string _absLibraryFolder = "";
    [ObservableProperty] private string _absConnectionStatus = "";
    [ObservableProperty] private string? _selectedAbsLibraryId;
    public ObservableCollection<AbsLibraryDisplayItem> AbsLibraries { get; } = [];

    public SettingsViewModel(
        IAbsApiClient absApiClient,
        AppSettings settings,
        ILogger<SettingsViewModel> logger)
    {
        _absApiClient = absApiClient;
        _settings = settings;
        _logger = logger;

        // Load current settings
        AbsServerUrl = settings.AbsServerUrl ?? "";
        AbsApiKey = settings.AbsApiKey ?? "";
        AbsLibraryFolder = settings.AbsLibraryFolder ?? "";
        SelectedAbsLibraryId = settings.AbsLibraryId;
    }

    [RelayCommand]
    private async Task TestAbsConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(AbsServerUrl) || string.IsNullOrWhiteSpace(AbsApiKey))
        {
            AbsConnectionStatus = "Server URL and API Key are required.";
            return;
        }

        AbsConnectionStatus = "Connecting...";
        AbsLibraries.Clear();

        try
        {
            _absApiClient.Configure(AbsServerUrl, AbsApiKey);
            var libraries = await _absApiClient.GetLibrariesAsync(ct);

            foreach (var lib in libraries)
            {
                AbsLibraries.Add(new AbsLibraryDisplayItem { Id = lib.Id, Name = lib.Name, MediaType = lib.MediaType });
            }

            AbsConnectionStatus = $"Connected. Found {libraries.Count} library(ies).";

            // Auto-select previously selected or first book library
            if (SelectedAbsLibraryId != null && AbsLibraries.Any(l => l.Id == SelectedAbsLibraryId))
            {
                // Keep existing selection
            }
            else
            {
                var bookLib = AbsLibraries.FirstOrDefault(l =>
                    l.MediaType.Equals("book", StringComparison.OrdinalIgnoreCase));
                if (bookLib != null)
                    SelectedAbsLibraryId = bookLib.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ABS connection test failed");
            AbsConnectionStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        _settings.AbsServerUrl = AbsServerUrl;
        _settings.AbsApiKey = AbsApiKey;
        _settings.AbsLibraryFolder = AbsLibraryFolder;
        _settings.AbsLibraryId = SelectedAbsLibraryId;

        var lib = AbsLibraries.FirstOrDefault(l => l.Id == SelectedAbsLibraryId);
        _settings.AbsLibraryName = lib?.Name;

        _settings.Save();

        // Configure the API client if settings are valid
        if (!string.IsNullOrWhiteSpace(AbsServerUrl) && !string.IsNullOrWhiteSpace(AbsApiKey))
        {
            _absApiClient.Configure(AbsServerUrl, AbsApiKey);
        }

        AbsConnectionStatus = "Settings saved.";
    }
}

/// <summary>
/// Display model for ABS library in the settings dropdown.
/// </summary>
public class AbsLibraryDisplayItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string MediaType { get; init; }
    public override string ToString() => $"{Name} ({MediaType})";
}
