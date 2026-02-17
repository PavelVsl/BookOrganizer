using BookOrganizer.Infrastructure.Database;
using BookOrganizer.Services.Audiobookshelf;
using BookOrganizer.Services.Deduplication;
using BookOrganizer.Services.Library;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Operations.FileOperators;
using BookOrganizer.Services.Preview;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.DependencyInjection;

namespace BookOrganizer.Infrastructure.Configuration;

/// <summary>
/// Extension methods for registering core (CLI-agnostic) services in the DI container.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds all core BookOrganizer services to the DI container.
    /// Does NOT include CLI-specific services (PreviewRenderer, DeduplicationResolver).
    /// </summary>
    public static IServiceCollection AddBookOrganizerCoreServices(this IServiceCollection services)
    {
        // Text normalization services
        services.AddSingleton<ITextNormalizer, TextNormalizer>();
        services.AddSingleton<INameDictionary, NameDictionary>();

        // Scanning services
        services.AddSingleton<IDirectoryScanner, DirectoryScanner>();

        // Metadata services
        services.AddSingleton<IMetadataJsonProcessor, MetadataJsonProcessor>();
        services.AddSingleton<IFolderHierarchyAnalyzer, FolderHierarchyAnalyzer>();
        services.AddSingleton<Mp3TagsCacheService>();
        services.AddSingleton<IMetadataExtractor, MetadataExtractor>();
        services.AddSingleton<IFilenameParser, FilenameParser>();
        services.AddSingleton<IMetadataConsolidator, MetadataConsolidator>();
        services.AddSingleton<IMetadataValidator, MetadataValidator>();
        services.AddSingleton<IMetadataGenerator, FolderStructureMetadataGenerator>();

        // Metadata formatters
        services.AddSingleton<IMetadataFormatter, BookOrganizerFormatter>();
        services.AddSingleton<IMetadataFormatter, AudiobookshelfFormatter>();
        services.AddSingleton<NfoFormatter>();
        services.AddSingleton<IMetadataFormatter>(sp => sp.GetRequiredService<NfoFormatter>());

        // Operation services
        services.AddSingleton<IPathGenerator, PathGenerator>();
        services.AddSingleton<IFilenameNormalizer, FilenameNormalizer>();
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

        // Preview services (generator only - renderer is CLI-specific)
        services.AddSingleton<IPreviewGenerator, PreviewGenerator>();

        // File organization services
        services.AddSingleton<IFileOrganizer, FileOrganizer>();

        // Deduplication services (detector + cache only - resolver is CLI-specific)
        services.AddSingleton<ContentAnalyzer>();
        services.AddSingleton<IDeduplicationDetector, DeduplicationDetector>();
        services.AddSingleton<IDeduplicationCache, InMemoryDeduplicationCache>();

        // Audiobookshelf services
        services.AddSingleton<IAbsApiClient, AbsApiClient>();
        services.AddSingleton<IPublishingService, PublishingService>();
        services.AddSingleton<AbsDeduplicationService>();

        // Library tree services
        services.AddTransient<ILibraryTree, LibraryTree>();

        return services;
    }
}
