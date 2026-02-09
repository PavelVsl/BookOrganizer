using System.CommandLine;
using System.Text.Json;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to export metadata files to audiobook folders.
/// Supports multiple output formats including Audiobookshelf-compatible formats.
/// </summary>
public class ExportMetadataCommand : Command
{
    public ExportMetadataCommand() : base("export-metadata", "Export metadata files to audiobook folders")
    {
        var sourceOption = new Option<string?>("--source", "-s")
        {
            Description = "Source directory to scan for audiobooks (or set BOOKORGANIZER_SOURCE env var)"
        };

        var formatOption = new Option<MetadataFormat>("--format", "-f")
        {
            Description = "Output format: bookorganizer, audiobookshelf, nfo, or all (or set BOOKORGANIZER_EXPORT_FORMAT env var)",
            DefaultValueFactory = _ =>
            {
                var envFormat = Environment.GetEnvironmentVariable("BOOKORGANIZER_EXPORT_FORMAT");
                if (envFormat != null && Enum.TryParse<MetadataFormat>(envFormat, ignoreCase: true, out var format))
                    return format;
                return MetadataFormat.BookOrganizer;
            }
        };

        var metadataSourceOption = new Option<string>("--metadata-source")
        {
            Description = "Source for metadata: 'mp3' (ID3 tags, default) or 'folder' (folder structure — sets source=manual)",
            DefaultValueFactory = _ => Environment.GetEnvironmentVariable("BOOKORGANIZER_METADATA_SOURCE") ?? "mp3"
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing metadata files"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output"
        };

        Options.Add(sourceOption);
        Options.Add(formatOption);
        Options.Add(metadataSourceOption);
        Options.Add(forceOption);
        Options.Add(verboseOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)
                ?? Environment.GetEnvironmentVariable("BOOKORGANIZER_SOURCE");
            var format = parseResult.GetValue(formatOption);
            var metadataSource = parseResult.GetValue(metadataSourceOption)!;
            var force = parseResult.GetValue(forceOption);
            var verbose = parseResult.GetValue(verboseOption);

            if (string.IsNullOrWhiteSpace(source))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] --source is required (or set BOOKORGANIZER_SOURCE env var)");
                return 1;
            }

            return await ExecuteAsync(source, format, metadataSource, force, verbose);
        });
    }

    private static async Task<int> ExecuteAsync(string sourcePath, MetadataFormat format, string metadataSource, bool force, bool verbose)
    {
        try
        {
            // Get services from DI
            var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
            var metadataExtractor = Program.ServiceProvider.GetRequiredService<IMetadataExtractor>();
            var formatters = Program.ServiceProvider.GetServices<IMetadataFormatter>().ToList();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<ExportMetadataCommand>>();
            var nameDictionary = Program.ServiceProvider.GetRequiredService<INameDictionary>();
            var folderHierarchyAnalyzer = Program.ServiceProvider.GetRequiredService<IFolderHierarchyAnalyzer>();
            var filenameParser = Program.ServiceProvider.GetRequiredService<IFilenameParser>();

            var useFolderSource = metadataSource.Equals("folder", StringComparison.OrdinalIgnoreCase);

            // Load name dictionary for diacritics restoration
            await nameDictionary.LoadAsync(sourcePath);

            if (!Directory.Exists(sourcePath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Source directory does not exist: {0}", sourcePath);
                return 1;
            }

            // Determine which formatters to use
            var selectedFormatters = GetFormattersForFormat(formatters, format);
            if (selectedFormatters.Count == 0)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] No formatters available for format: {0}", format);
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Scanning directory:[/] {0}", sourcePath);
            AnsiConsole.MarkupLine("[dim]Output format(s):[/] {0}", string.Join(", ", selectedFormatters.Select(f => f.FormatName)));
            AnsiConsole.MarkupLine("[dim]Metadata source:[/] {0}", useFolderSource ? "folder structure" : "MP3 tags");
            AnsiConsole.WriteLine();

            // Scan for audiobook folders
            var folders = await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[yellow]Scanning directories...[/]");
                    task.IsIndeterminate = true;

                    var progress = new Progress<ScanProgress>(p =>
                    {
                        task.Description = $"[yellow]Scanning:[/] {p.CurrentDirectory ?? "..."}";
                    });

                    var result = await scanner.ScanDirectoryAsync(sourcePath, progress, CancellationToken.None);

                    task.StopTask();
                    return result;
                });

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Found {0} audiobook folder(s)[/]", folders.Count);
            AnsiConsole.WriteLine();

            if (folders.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No audiobook folders found.[/]");
                return 0;
            }

            // Extract and export metadata
            var exportedCount = 0;
            var skippedCount = 0;
            var errorCount = 0;

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[yellow]Exporting metadata...[/]", maxValue: folders.Count);

                    foreach (var folder in folders)
                    {
                        try
                        {
                            var bookName = Path.GetFileName(folder.Path);
                            task.Description = $"[yellow]Processing:[/] {bookName}";

                            if (useFolderSource)
                            {
                                // Folder-based extraction: write bookinfo.json from folder structure
                                var metadataFilePath = Path.Combine(folder.Path, "bookinfo.json");

                                // Protect manually-edited files
                                if (await MetadataJsonProcessor.IsManuallyEditedAsync(metadataFilePath))
                                {
                                    AnsiConsole.MarkupLine("[yellow]Protected:[/] {0} (source=manual)", bookName);
                                    skippedCount++;
                                    task.Increment(1);
                                    continue;
                                }

                                if (File.Exists(metadataFilePath) && !force)
                                {
                                    if (verbose)
                                        AnsiConsole.MarkupLine("[dim]Skipped:[/] {0}", bookName);
                                    skippedCount++;
                                    task.Increment(1);
                                    continue;
                                }

                                var hierarchyInfo = folderHierarchyAnalyzer.AnalyzeHierarchy(folder.Path, sourcePath);
                                var folderName = Path.GetFileName(folder.Path);
                                var filenameInfo = filenameParser.ParseFolderPath(folderName);

                                var metadataOverride = new MetadataOverride
                                {
                                    Title = filenameInfo.Title,
                                    Author = hierarchyInfo?.Author ?? filenameInfo.Author,
                                    Series = hierarchyInfo?.Series ?? filenameInfo.Series,
                                    SeriesNumber = filenameInfo.SeriesNumber,
                                    Source = "manual"
                                };

                                var jsonOptions = new JsonSerializerOptions
                                {
                                    WriteIndented = true,
                                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                                };

                                var json = JsonSerializer.Serialize(metadataOverride, jsonOptions);
                                await File.WriteAllTextAsync(metadataFilePath, json);
                                exportedCount++;

                                if (verbose)
                                    AnsiConsole.MarkupLine("[green]✓[/] Exported: [dim]{0}[/]", metadataFilePath);
                            }
                            else
                            {
                                // MP3 tag extraction (default)
                                var metadata = await metadataExtractor.ExtractMetadataAsync(folder, null, CancellationToken.None);

                                var folderExported = false;
                                var folderSkipped = true;

                                foreach (var formatter in selectedFormatters)
                                {
                                    var metadataFilePath = Path.Combine(folder.Path, formatter.FileName);

                                    // Protect manually-edited bookinfo.json files (source=manual)
                                    if (formatter is BookOrganizerFormatter &&
                                        await MetadataJsonProcessor.IsManuallyEditedAsync(metadataFilePath))
                                    {
                                        AnsiConsole.MarkupLine(
                                            "[yellow]Protected:[/] {0} (source=manual)",
                                            bookName);
                                        continue;
                                    }

                                    // Skip if file exists and force is not set
                                    if (File.Exists(metadataFilePath) && !force)
                                    {
                                        if (verbose)
                                        {
                                            AnsiConsole.MarkupLine(
                                                "[dim]Skipped:[/] {0} ({1})",
                                                bookName,
                                                formatter.FormatName);
                                        }
                                        continue;
                                    }

                                    folderSkipped = false;

                                    // Format and write metadata
                                    var formattedContent = await formatter.FormatAsync(metadata);
                                    await File.WriteAllTextAsync(metadataFilePath, formattedContent);

                                    folderExported = true;

                                    if (verbose)
                                    {
                                        AnsiConsole.MarkupLine(
                                            "[green]✓[/] Exported: [dim]{0}[/] ({1})",
                                            metadataFilePath,
                                            formatter.FormatName);
                                    }
                                }

                                if (folderExported)
                                {
                                    exportedCount++;
                                }
                                else if (folderSkipped)
                                {
                                    skippedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            logger.LogError(ex, "Failed to export metadata for {Path}", folder.Path);

                            if (verbose)
                            {
                                AnsiConsole.MarkupLine(
                                    "[red]✗[/] Error: [dim]{0}[/] - {1}",
                                    folder.Path,
                                    ex.Message);
                            }
                        }

                        task.Increment(1);
                    }
                });

            // Generate bookinfo.json for parent folders (author/series levels)
            var metadataGenerator = Program.ServiceProvider.GetRequiredService<IMetadataGenerator>();
            var audiobookPaths = new HashSet<string>(
                folders.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

            // Collect all intermediate directories (author, series) up to 3 levels
            var parentFolders = new List<string>();
            foreach (var topDir in Directory.GetDirectories(sourcePath))
            {
                parentFolders.Add(topDir);
                foreach (var subDir in Directory.GetDirectories(topDir))
                {
                    parentFolders.Add(subDir);
                    foreach (var deepDir in Directory.GetDirectories(subDir))
                    {
                        parentFolders.Add(deepDir);
                    }
                }
            }

            // Filter to non-audiobook folders only
            var intermediateFolders = parentFolders
                .Where(p => !audiobookPaths.Contains(p))
                .ToList();

            if (intermediateFolders.Count > 0)
            {
                var parentExported = 0;
                var parentSkipped = 0;

                await AnsiConsole.Progress()
                    .AutoRefresh(true)
                    .AutoClear(false)
                    .Columns(
                        new TaskDescriptionColumn(),
                        new ProgressBarColumn(),
                        new PercentageColumn(),
                        new SpinnerColumn())
                    .StartAsync(async ctx =>
                    {
                        var task = ctx.AddTask("[yellow]Generating parent folder metadata...[/]", maxValue: intermediateFolders.Count);

                        foreach (var folderPath in intermediateFolders)
                        {
                            try
                            {
                                task.Description = $"[yellow]Processing:[/] {Path.GetFileName(folderPath)}";

                                var result = await metadataGenerator.GenerateMetadataFromStructureAsync(
                                    folderPath, sourcePath, format, force, CancellationToken.None);

                                if (result.Success)
                                {
                                    if (result.Skipped)
                                        parentSkipped++;
                                    else
                                    {
                                        parentExported++;
                                        if (verbose)
                                            AnsiConsole.MarkupLine("[green]✓[/] Parent: [dim]{0}[/]", result.FilePath);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                errorCount++;
                                logger.LogError(ex, "Failed to generate parent metadata for {Path}", folderPath);
                            }

                            task.Increment(1);
                        }
                    });

                exportedCount += parentExported;
                skippedCount += parentSkipped;

                AnsiConsole.MarkupLine("[dim]Parent folders:[/] {0} exported, {1} skipped",
                    parentExported, parentSkipped);
            }

            // Display summary
            AnsiConsole.WriteLine();
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[yellow]Result[/]");
            table.AddColumn(new TableColumn("[yellow]Count[/]").RightAligned());

            table.AddRow("[green]Exported[/]", exportedCount.ToString());
            table.AddRow("[dim]Skipped[/]", skippedCount.ToString());
            table.AddRow("[red]Errors[/]", errorCount.ToString());
            table.AddRow("[bold]Total[/]", (folders.Count + intermediateFolders.Count).ToString());

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            if (skippedCount > 0)
            {
                AnsiConsole.MarkupLine("[dim]Tip: Use --force to overwrite existing metadata files[/]");
            }

            return errorCount > 0 ? 1 : 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", ex.Message);
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
    }

    /// <summary>
    /// Gets the formatters for the specified format.
    /// </summary>
    private static List<IMetadataFormatter> GetFormattersForFormat(List<IMetadataFormatter> allFormatters, MetadataFormat format)
    {
        return format switch
        {
            MetadataFormat.BookOrganizer => allFormatters.Where(f => f is BookOrganizerFormatter).ToList(),
            MetadataFormat.Audiobookshelf => allFormatters.Where(f => f is AudiobookshelfFormatter).ToList(),
            MetadataFormat.Nfo => allFormatters.Where(f => f is NfoFormatter).ToList(),
            MetadataFormat.All => allFormatters,
            _ => []
        };
    }
}
