using System;
using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

        // Register ViewModels
        services.AddTransient<LibraryViewModel>();
        services.AddTransient<BatchRenameViewModel>();
        services.AddTransient<ScanViewModel>();
        services.AddTransient<PreviewViewModel>();
        services.AddTransient<OrganizeViewModel>();
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<MainWindowViewModel>();

        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = Services.GetRequiredService<MainWindowViewModel>();

            // Restore last nav selection
            mainVm.SelectedNavIndex = Settings.SelectedNavIndex;

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
        }

        base.OnFrameworkInitializationCompleted();
    }
}
