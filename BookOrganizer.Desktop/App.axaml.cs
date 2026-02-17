using System;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Register settings as singleton so all VMs share the same instance
        services.AddSingleton(Settings);

        // Register UI-layer services
        services.AddSingleton<PublishQueueService>();

        // Register ViewModels
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<AbsLibraryViewModel>();
        services.AddTransient<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();

            // Restore last nav selection (clamped to valid range)
            mainVm.SelectedNavIndex = Math.Min(Settings.SelectedNavIndex, 2);

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
                Settings.SelectedNavIndex = mainVm.SelectedNavIndex;
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
}
