using BookOrganizer.Rendering;
using BookOrganizer.Services.Deduplication;
using BookOrganizer.Services.Preview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Infrastructure.Configuration;

/// <summary>
/// Extension methods for configuring CLI-specific services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all application services (core + CLI-specific) to the DI container.
    /// </summary>
    public static IServiceCollection AddBookOrganizerServices(this IServiceCollection services)
    {
        // Register all core (CLI-agnostic) services
        services.AddBookOrganizerCoreServices();

        // CLI-specific services (Spectre.Console dependent)
        services.AddSingleton<IPreviewRenderer, PreviewRenderer>();
        services.AddSingleton<IDeduplicationResolver, DeduplicationResolver>();

        return services;
    }

    /// <summary>
    /// Adds logging configuration to the DI container.
    /// Default log level is Warning for regular use.
    /// Set BOOKORGANIZER_LOG_LEVEL environment variable to "Information" or "Debug" for verbose output.
    /// </summary>
    public static IServiceCollection AddBookOrganizerLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();

            // Read log level from environment variable, default to Warning
            var logLevelStr = Environment.GetEnvironmentVariable("BOOKORGANIZER_LOG_LEVEL");
            var minLevel = LogLevel.Warning;

            if (!string.IsNullOrEmpty(logLevelStr) && Enum.TryParse<LogLevel>(logLevelStr, true, out var parsedLevel))
            {
                minLevel = parsedLevel;
            }

            builder.SetMinimumLevel(minLevel);
        });

        return services;
    }
}
