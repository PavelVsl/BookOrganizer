using System;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using BookOrganizer.Desktop.Services;
using BookOrganizer.Desktop.ViewModels;
using BookOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = null!;

    private MainWindowViewModel? MainVm =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.DataContext as MainWindowViewModel;

    private void OnAboutClick(object? sender, EventArgs e) =>
        MainVm?.ShowAboutCommand.Execute(null);

    private void OnSettingsClick(object? sender, EventArgs e) =>
        MainVm?.OpenSettingsCommand.Execute(null);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Register code pages for Czech encoding support
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Load persistent settings
        Settings = AppSettings.Load();

        // Build DI container
        var services = new ServiceCollection();
        services.AddBookOrganizerCoreServices();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddDebug();
        });

        // Register settings as singleton so all VMs share the same instance
        services.AddSingleton(Settings);

        // Register UI-layer services
        services.AddSingleton<PublishQueueService>();

        // Register ViewModels
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<AbsLibraryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();

            var mainWindow = new MainWindow
            {
                DataContext = mainVm,
                Width = Settings.WindowWidth,
                Height = Settings.WindowHeight
            };

            // Save settings on close
            mainWindow.Closing += (_, _) =>
            {
                Settings.WindowWidth = mainWindow.Width;
                Settings.WindowHeight = mainWindow.Height;
                Settings.Save();
            };

            desktop.MainWindow = mainWindow;

            // Auto-load last library if path was persisted
            if (!string.IsNullOrEmpty(mainVm.Library.LibraryPath))
            {
                mainVm.Library.LoadLibraryCommand.ExecuteAsync(null);
            }

            // Try connecting to ABS in background
            _ = mainVm.TryConnectAbsAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the native menu bar and attaches it to a window.
    /// Call this on any window that should show the macOS native menu.
    /// </summary>
    public static void SetNativeMenu(Window window)
    {
        MainWindowViewModel? GetVm() =>
            (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.DataContext as MainWindowViewModel;

        var fileMenu = new NativeMenu();
        var openLib = new NativeMenuItem("Open Library...") { Gesture = new KeyGesture(Key.O, KeyModifiers.Meta) };
        openLib.Click += (_, _) => GetVm()?.OpenLibraryCommand.Execute(null);
        fileMenu.Items.Add(openLib);
        var settings = new NativeMenuItem("Settings...") { Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta) };
        settings.Click += (_, _) => GetVm()?.OpenSettingsCommand.Execute(null);
        fileMenu.Items.Add(settings);

        var libraryMenu = new NativeMenu();
        var refresh = new NativeMenuItem("Refresh") { Gesture = new KeyGesture(Key.R, KeyModifiers.Meta) };
        refresh.Click += (_, _) => GetVm()?.Library.LoadLibraryCommand.Execute(null);
        libraryMenu.Items.Add(refresh);
        var scanMeta = new NativeMenuItem("Scan Metadata");
        scanMeta.Click += (_, _) => GetVm()?.Library.ScanMetadataCommand.Execute(null);
        libraryMenu.Items.Add(scanMeta);
        var exportNfo = new NativeMenuItem("Export NFO");
        exportNfo.Click += (_, _) => GetVm()?.Library.ExportNfoCommand.Execute(null);
        libraryMenu.Items.Add(exportNfo);
        libraryMenu.Items.Add(new NativeMenuItemSeparator());
        var reorg = new NativeMenuItem("Reorganize");
        reorg.Click += (_, _) => GetVm()?.Library.ReorganizeCommand.Execute(null);
        libraryMenu.Items.Add(reorg);
        var verify = new NativeMenuItem("Verify Library");
        verify.Click += (_, _) => GetVm()?.Library.VerifyLibraryCommand.Execute(null);
        libraryMenu.Items.Add(verify);
        var synonyms = new NativeMenuItem("Detect Synonyms");
        synonyms.Click += (_, _) => GetVm()?.Library.DetectSynonymsCommand.Execute(null);
        libraryMenu.Items.Add(synonyms);

        var absMenu = new NativeMenu();
        var publishAll = new NativeMenuItem("Publish All");
        publishAll.Click += (_, _) => GetVm()?.Library.PublishAllCommand.Execute(null);
        absMenu.Items.Add(publishAll);
        var checkDups = new NativeMenuItem("Check Duplicates");
        checkDups.Click += (_, _) => GetVm()?.Library.CheckAbsDuplicatesCommand.Execute(null);
        absMenu.Items.Add(checkDups);
        absMenu.Items.Add(new NativeMenuItemSeparator());
        var refreshAbs = new NativeMenuItem("Refresh ABS Library");
        refreshAbs.Click += (_, _) => GetVm()?.Library.AbsLibraryVm.RefreshCommand.Execute(null);
        absMenu.Items.Add(refreshAbs);

        var helpMenu = new NativeMenu();
        var about = new NativeMenuItem("About BookOrganizer");
        about.Click += (_, _) => GetVm()?.ShowAboutCommand.Execute(null);
        helpMenu.Items.Add(about);

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("File") { Menu = fileMenu });
        menu.Items.Add(new NativeMenuItem("Library") { Menu = libraryMenu });
        menu.Items.Add(new NativeMenuItem("Audiobookshelf") { Menu = absMenu });
        menu.Items.Add(new NativeMenuItem("Help") { Menu = helpMenu });

        NativeMenu.SetMenu(window, menu);
    }
}
