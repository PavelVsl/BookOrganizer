using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Infrastructure.Configuration;

/// <summary>
/// Extension methods for configuring services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds core application services to the DI container.
    /// </summary>
    public static IServiceCollection AddBookOrganizerServices(this IServiceCollection services)
    {
        // Scanning services
        services.AddSingleton<IDirectoryScanner, DirectoryScanner>();

        // Metadata services
        services.AddSingleton<IMetadataExtractor, MetadataExtractor>();
        services.AddSingleton<IFilenameParser, FilenameParser>();

        // Additional services will be registered here as they are implemented
        // services.AddSingleton<IFileOrganizer, FileOrganizer>();
        // services.AddSingleton<IPathGenerator, PathGenerator>();

        return services;
    }

    /// <summary>
    /// Adds logging configuration to the DI container.
    /// </summary>
    public static IServiceCollection AddBookOrganizerLogging(this IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        return services;
    }
}
