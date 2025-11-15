using BookOrganizer.Services.Deduplication;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Operations.FileOperators;
using BookOrganizer.Services.Preview;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Text;
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
        // Text normalization services
        services.AddSingleton<ITextNormalizer, TextNormalizer>();

        // Scanning services
        services.AddSingleton<IDirectoryScanner, DirectoryScanner>();

        // Metadata services
        services.AddSingleton<IMetadataExtractor, MetadataExtractor>();
        services.AddSingleton<IFilenameParser, FilenameParser>();
        services.AddSingleton<IMetadataConsolidator, MetadataConsolidator>();
        services.AddSingleton<IMetadataValidator, MetadataValidator>();

        // Operation services
        services.AddSingleton<IPathGenerator, PathGenerator>();
        services.AddSingleton<ChecksumCalculator>();

        // File operator services
        services.AddSingleton<CopyFileOperator>();
        services.AddSingleton<MoveFileOperator>();
        services.AddSingleton<HardLinkFileOperator>();
        services.AddSingleton<SymbolicLinkFileOperator>();

        // Register all specific file operators as ISpecificFileOperator
        services.AddSingleton<ISpecificFileOperator>(sp => sp.GetRequiredService<CopyFileOperator>());
        services.AddSingleton<ISpecificFileOperator>(sp => sp.GetRequiredService<MoveFileOperator>());
        services.AddSingleton<ISpecificFileOperator>(sp => sp.GetRequiredService<HardLinkFileOperator>());
        services.AddSingleton<ISpecificFileOperator>(sp => sp.GetRequiredService<SymbolicLinkFileOperator>());

        // Main file operator orchestrator
        services.AddSingleton<IFileOperator, FileOperator>();

        // Preview services
        services.AddSingleton<IPreviewGenerator, PreviewGenerator>();
        services.AddSingleton<IPreviewRenderer, PreviewRenderer>();

        // File organization services
        services.AddSingleton<IFileOrganizer, FileOrganizer>();

        // Deduplication services
        services.AddSingleton<ContentAnalyzer>();
        services.AddSingleton<IDeduplicationDetector, DeduplicationDetector>();
        services.AddSingleton<IDeduplicationResolver, DeduplicationResolver>();
        services.AddSingleton<IDeduplicationCache, InMemoryDeduplicationCache>();

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
