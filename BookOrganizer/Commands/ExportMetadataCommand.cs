using System.CommandLine;
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
        var sourceOption = new Option<string>("--source", "-s")
        {
            Description = "Source directory to scan for audiobooks",
            Required = true
        };

        var formatOption = new Option<MetadataFormat>("--format", "-f")
        {
            Description = "Output format: bookorganizer (default), audiobookshelf, nfo, or all",
            DefaultValueFactory = _ => MetadataFormat.BookOrganizer
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
        Options.Add(forceOption);
        Options.Add(verboseOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)!;
            var format = parseResult.GetValue(formatOption);
            var force = parseResult.GetValue(forceOption);
            var verbose = parseResult.GetValue(verboseOption);
            return await ExecuteAsync(source, format, force, verbose);
        });
    }

    private static async Task<int> ExecuteAsync(string sourcePath, MetadataFormat format, bool force, bool verbose)
    {
        try
        {
            // Get services from DI
            var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
            var metadataExtractor = Program.ServiceProvider.GetRequiredService<IMetadataExtractor>();
            var formatters = Program.ServiceProvider.GetServices<IMetadataFormatter>().ToList();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<ExportMetadataCommand>>();
            var nameDictionary = Program.ServiceProvider.GetRequiredService<INameDictionary>();

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

                            // Extract metadata
                            var metadata = await metadataExtractor.ExtractMetadataAsync(folder, null, CancellationToken.None);

                            var folderExported = false;
                            var folderSkipped = true;

                            foreach (var formatter in selectedFormatters)
                            {
                                var metadataFilePath = Path.Combine(folder.Path, formatter.FileName);

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

            // Display summary
            AnsiConsole.WriteLine();
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[yellow]Result[/]");
            table.AddColumn(new TableColumn("[yellow]Count[/]").RightAligned());

            table.AddRow("[green]Exported[/]", exportedCount.ToString());
            table.AddRow("[dim]Skipped[/]", skippedCount.ToString());
            table.AddRow("[red]Errors[/]", errorCount.ToString());
            table.AddRow("[bold]Total[/]", folders.Count.ToString());

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
