using System;
using System.Threading.Tasks;
using BookOrganizer.Desktop.Services;
using BookOrganizer.Services.Audiobookshelf;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private int _selectedNavIndex;

    // ABS status line
    [ObservableProperty] private string _absStatusText = "";
    [ObservableProperty] private bool _absConnected;

    public LibraryViewModel Library => _libraryViewModel;
    public PublishQueueService PublishQueue { get; }

    private readonly LibraryViewModel _libraryViewModel;
    private readonly ToolsViewModel _toolsViewModel;
    private readonly AbsLibraryViewModel _absLibraryViewModel;
    private readonly IAbsApiClient _absApiClient;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        ToolsViewModel toolsViewModel,
        AbsLibraryViewModel absLibraryViewModel,
        PublishQueueService publishQueue,
        IAbsApiClient absApiClient,
        AppSettings settings,
        ILogger<MainWindowViewModel> logger)
    {
        _libraryViewModel = libraryViewModel;
        _toolsViewModel = toolsViewModel;
        _absLibraryViewModel = absLibraryViewModel;
        PublishQueue = publishQueue;
        _absApiClient = absApiClient;
        _settings = settings;
        _logger = logger;
        _currentView = libraryViewModel;
    }

    /// <summary>
    /// Attempts to connect to ABS on startup if settings are configured.
    /// Called from App.axaml.cs after construction.
    /// </summary>
    public async Task TryConnectAbsAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.AbsServerUrl) ||
            string.IsNullOrWhiteSpace(_settings.AbsApiKey))
        {
            AbsStatusText = "ABS: not configured";
            return;
        }

        AbsStatusText = "ABS: connecting...";

        try
        {
            _absApiClient.Configure(_settings.AbsServerUrl, _settings.AbsApiKey);
            var libraries = await _absApiClient.GetLibrariesAsync();

            AbsConnected = true;

            if (!string.IsNullOrWhiteSpace(_settings.AbsLibraryId))
            {
                var lib = libraries.Find(l => l.Id == _settings.AbsLibraryId);
                AbsStatusText = lib != null
                    ? $"ABS: {lib.Name}"
                    : $"ABS: connected (library not found)";
            }
            else
            {
                AbsStatusText = $"ABS: connected ({libraries.Count} libraries)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to ABS on startup");
            AbsConnected = false;
            AbsStatusText = "ABS: offline";
        }
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        CurrentView = value switch
        {
            0 => _libraryViewModel,
            1 => _toolsViewModel,
            2 => _absLibraryViewModel,
            _ => _libraryViewModel
        };
    }
}
