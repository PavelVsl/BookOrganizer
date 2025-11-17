using System.CommandLine;
using System.Text.Json;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to export metadata.json files to audiobook folders.
/// </summary>
public class ExportMetadataCommand : Command
{
    public ExportMetadataCommand() : base("export-metadata", "Export metadata.json files to audiobook folders")
    {
        var sourceOption = new Option<string>(
            aliases: ["--source", "-s"],
            description: "Source directory to scan for audiobooks")
        {
            IsRequired = true
        };

        var forceOption = new Option<bool>(
            aliases: ["--force", "-f"],
            description: "Overwrite existing metadata.json files");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        AddOption(sourceOption);
        AddOption(forceOption);
        AddOption(verboseOption);

        this.SetHandler(ExecuteAsync, sourceOption, forceOption, verboseOption);
    }

    private static async Task<int> ExecuteAsync(string sourcePath, bool force, bool verbose)
    {
        try
        {
            // Get services from DI
            var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
            var metadataExtractor = Program.ServiceProvider.GetRequiredService<IMetadataExtractor>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<ExportMetadataCommand>>();

            if (!Directory.Exists(sourcePath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Source directory does not exist: {0}", sourcePath);
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Scanning directory:[/] {0}", sourcePath);
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
                            var metadataFilePath = Path.Combine(folder.Path, "metadata.json");

                            // Skip if file exists and force is not set
                            if (System.IO.File.Exists(metadataFilePath) && !force)
                            {
                                skippedCount++;
                                task.Description = $"[dim]Skipped:[/] {Path.GetFileName(folder.Path)}";
                                task.Increment(1);
                                continue;
                            }

                            task.Description = $"[yellow]Processing:[/] {Path.GetFileName(folder.Path)}";

                            // Extract metadata
                            var metadata = await metadataExtractor.ExtractMetadataAsync(folder, null, CancellationToken.None);

                            // Create metadata override from extracted data
                            var metadataOverride = new MetadataOverride
                            {
                                Title = metadata.Title,
                                Author = metadata.Author,
                                Narrator = metadata.Narrator,
                                Series = metadata.Series,
                                SeriesNumber = metadata.SeriesNumber,
                                Year = metadata.Year,
                                Genre = metadata.Genre,
                                Description = metadata.Description
                            };

                            // Serialize to JSON
                            var options = new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                            };

                            var json = JsonSerializer.Serialize(metadataOverride, options);

                            // Write to file
                            await System.IO.File.WriteAllTextAsync(metadataFilePath, json);

                            exportedCount++;

                            if (verbose)
                            {
                                AnsiConsole.MarkupLine(
                                    "[green]✓[/] Exported: [dim]{0}[/]",
                                    metadataFilePath);
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
                AnsiConsole.MarkupLine("[dim]Tip: Use --force to overwrite existing metadata.json files[/]");
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
}
