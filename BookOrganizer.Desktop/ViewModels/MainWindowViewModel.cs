using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using BookOrganizer.Desktop.Services;
using BookOrganizer.Desktop.Views;
using BookOrganizer.Services.Audiobookshelf;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    // ABS status line
    [ObservableProperty] private string _absStatusText = "";
    [ObservableProperty] private bool _absConnected;

    public LibraryViewModel Library { get; }
    public PublishQueueService PublishQueue { get; }

    private readonly IAbsApiClient _absApiClient;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        PublishQueueService publishQueue,
        IAbsApiClient absApiClient,
        AppSettings settings,
        ILogger<MainWindowViewModel> logger)
    {
        Library = libraryViewModel;
        PublishQueue = publishQueue;
        _absApiClient = absApiClient;
        _settings = settings;
        _logger = logger;
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

    [RelayCommand]
    private async Task OpenLibraryAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select library folder",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath();
            if (path != null)
            {
                Library.LibraryPath = path;
                await Library.LoadLibraryCommand.ExecuteAsync(null);
            }
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var settingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsVm
        };

        // Apply native menu so macOS menu bar persists while dialog is open
        App.SetNativeMenu(settingsWindow);

        var result = await settingsWindow.ShowDialog<bool?>(desktop.MainWindow);
        if (result == true)
        {
            // Re-check ABS connection with new settings
            _ = TryConnectAbsAsync();
        }
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "dev";

        var dialog = new Window
        {
            Title = "About BookOrganizer",
            Width = 340,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "BookOrganizer", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.Bold },
                    new TextBlock { Text = $"Version {versionStr}" },
                    new TextBlock { Text = "Audiobook library organizer", Opacity = 0.7 },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }
                }
            }
        };

        // Wire OK button to close
        if (dialog.Content is StackPanel sp && sp.Children[^1] is Button okBtn)
        {
            okBtn.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(desktop.MainWindow);
    }
}
