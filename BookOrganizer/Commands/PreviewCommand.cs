using BookOrganizer.Models;
using BookOrganizer.Services.Preview;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;
using System.Text.Json;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to preview organization operations without executing them.
/// </summary>
public class PreviewCommand : Command
{
    public PreviewCommand() : base("preview", "Preview audiobook organization without executing")
    {
        var sourceOption = new Option<string>(
            aliases: ["--source", "-s"],
            description: "Source directory containing audiobooks")
        {
            IsRequired = true
        };

        var destinationOption = new Option<string>(
            aliases: ["--destination", "-d", "--library", "-l"],
            description: "Target library directory for organized audiobooks")
        {
            IsRequired = true
        };

        var operationOption = new Option<string>(
            aliases: ["--operation", "-o"],
            description: "Operation type: copy, move, hardlink, symlink",
            getDefaultValue: () => "copy");

        var exportOption = new Option<string?>(
            aliases: ["--export", "-e"],
            description: "Export preview to file (supports .json, .csv, .txt)");

        var authorOption = new Option<string?>(
            aliases: ["--author", "-a"],
            description: "Filter by author name (case-insensitive)");

        var seriesOption = new Option<string?>(
            aliases: ["--series"],
            description: "Filter by series name (case-insensitive)");

        var maxItemsOption = new Option<int?>(
            aliases: ["--max-items", "-m"],
            description: "Maximum number of audiobooks to show");

        var compactOption = new Option<bool>(
            aliases: ["--compact", "-c"],
            description: "Use compact display mode");

        var noTreeOption = new Option<bool>(
            aliases: ["--no-tree"],
            description: "Don't show tree view");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        var detectDuplicatesOption = new Option<bool>(
            aliases: ["--detect-duplicates"],
            description: "Detect potential duplicate audiobooks");

        var duplicateThresholdOption = new Option<double>(
            aliases: ["--duplicate-threshold"],
            description: "Minimum confidence for duplicate detection (0.0-1.0)",
            getDefaultValue: () => 0.7);

        var rebuildCacheOption = new Option<bool>(
            aliases: ["--rebuild-cache"],
            description: "Force rebuild of library metadata cache");

        var exportMetadataOption = new Option<bool>(
            aliases: ["--export-metadata"],
            description: "Export metadata.json files to source audiobook folders for editing");

        var interactiveOption = new Option<bool>(
            aliases: ["--interactive", "-i"],
            description: "Prompt to organize immediately after successful preview");

        AddOption(sourceOption);
        AddOption(destinationOption);
        AddOption(operationOption);
        AddOption(exportOption);
        AddOption(authorOption);
        AddOption(seriesOption);
        AddOption(maxItemsOption);
        AddOption(compactOption);
        AddOption(noTreeOption);
        AddOption(verboseOption);
        AddOption(detectDuplicatesOption);
        AddOption(duplicateThresholdOption);
        AddOption(rebuildCacheOption);
        AddOption(exportMetadataOption);
        AddOption(interactiveOption);

        this.SetHandler(async (context) =>
        {
            var source = context.ParseResult.GetValueForOption(sourceOption)!;
            var destination = context.ParseResult.GetValueForOption(destinationOption)!;
            var operation = context.ParseResult.GetValueForOption(operationOption)!;
            var export = context.ParseResult.GetValueForOption(exportOption);
            var authorFilter = context.ParseResult.GetValueForOption(authorOption);
            var seriesFilter = context.ParseResult.GetValueForOption(seriesOption);
            var maxItemsFilter = context.ParseResult.GetValueForOption(maxItemsOption);
            var compactMode = context.ParseResult.GetValueForOption(compactOption);
            var noTreeMode = context.ParseResult.GetValueForOption(noTreeOption);
            var verboseMode = context.ParseResult.GetValueForOption(verboseOption);
            var detectDuplicates = context.ParseResult.GetValueForOption(detectDuplicatesOption);
            var duplicateThreshold = context.ParseResult.GetValueForOption(duplicateThresholdOption);
            var rebuildCache = context.ParseResult.GetValueForOption(rebuildCacheOption);
            var exportMetadata = context.ParseResult.GetValueForOption(exportMetadataOption);
            var interactive = context.ParseResult.GetValueForOption(interactiveOption);

            var exitCode = await ExecuteAsync(
                source, destination, operation, export,
                authorFilter, seriesFilter, maxItemsFilter,
                compactMode, noTreeMode, verboseMode,
                detectDuplicates, duplicateThreshold, rebuildCache,
                exportMetadata, interactive);

            context.ExitCode = exitCode;
        });
    }

    private static async Task<int> ExecuteAsync(
        string sourcePath,
        string destinationPath,
        string operationType,
        string? exportPath,
        string? author,
        string? series,
        int? maxItems,
        bool compact,
        bool noTree,
        bool verbose,
        bool detectDuplicates,
        double duplicateThreshold,
        bool rebuildCache,
        bool exportMetadata,
        bool interactive)
    {
        try
        {
            // Get services from DI
            var previewGenerator = Program.ServiceProvider.GetRequiredService<IPreviewGenerator>();
            var previewRenderer = Program.ServiceProvider.GetRequiredService<IPreviewRenderer>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<PreviewCommand>>();

            // Validate directories
            if (!Directory.Exists(sourcePath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Source directory does not exist: {0}", sourcePath);
                return 1;
            }

            // Parse operation type
            if (!Enum.TryParse<FileOperationType>(operationType, ignoreCase: true, out var opType))
            {
                AnsiConsole.MarkupLine(
                    "[red]Error:[/] Invalid operation type '{0}'. Valid options: copy, move, hardlink, symlink",
                    operationType);
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Generating preview...[/]");
            AnsiConsole.WriteLine();

            // Build filter
            var filter = new PreviewFilter
            {
                Authors = author != null ? new[] { author } : null,
                Series = series != null ? new[] { series } : null,
                MaxItems = maxItems
            };

            // Generate preview with progress
            var preview = await AnsiConsole.Status()
                .StartAsync("[yellow]Analyzing audiobooks...[/]", async ctx =>
                {
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(Style.Parse("yellow"));

                    return await previewGenerator.GeneratePreviewAsync(
                        sourcePath,
                        destinationPath,
                        opType,
                        filter,
                        detectDuplicates,
                        duplicateThreshold,
                        rebuildCache,
                        CancellationToken.None);
                });

            AnsiConsole.WriteLine();

            // Display preview
            var renderOptions = new PreviewRenderOptions
            {
                ShowTree = !noTree,
                CompactMode = compact,
                ShowFullPaths = verbose,
                MaxOperationsToShow = maxItems
            };

            previewRenderer.RenderPreview(preview, renderOptions);

            // Export if requested
            if (!string.IsNullOrEmpty(exportPath))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Exporting preview...[/]");

                var format = DetermineExportFormat(exportPath);
                await previewGenerator.ExportPreviewAsync(preview, exportPath, format, CancellationToken.None);

                AnsiConsole.MarkupLine("[green]✓[/] Preview exported to: {0}", exportPath);
            }

            // Export metadata if requested
            if (exportMetadata)
            {
                AnsiConsole.WriteLine();
                await ExportMetadataAsync(sourcePath, verbose);
            }

            // Show summary
            AnsiConsole.WriteLine();
            var hasErrors = preview.Statistics.IssueCounts[IssueSeverity.Error] > 0;
            var hasWarnings = preview.Statistics.IssueCounts[IssueSeverity.Warning] > 0;

            if (hasErrors)
            {
                AnsiConsole.MarkupLine(
                    "[red]⚠ Preview contains {0} error(s) that must be resolved before organizing[/]",
                    preview.Statistics.IssueCounts[IssueSeverity.Error]);
            }
            else if (hasWarnings)
            {
                AnsiConsole.MarkupLine(
                    "[yellow]⚠ Preview contains {0} warning(s) - review before organizing[/]",
                    preview.Statistics.IssueCounts[IssueSeverity.Warning]);
            }
            else
            {
                AnsiConsole.MarkupLine("[green]✓ No issues detected - ready to organize![/]");
            }

            // Interactive mode - prompt to organize if no errors
            if (interactive && !hasErrors && preview.Operations.Count > 0)
            {
                AnsiConsole.WriteLine();

                if (AnsiConsole.Confirm("[bold]Do you want to organize these audiobooks now?[/]", defaultValue: false))
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[green]Starting organization...[/]");
                    AnsiConsole.WriteLine();

                    // Execute organization
                    var exitCode = await ExecuteOrganizeAsync(
                        sourcePath,
                        destinationPath,
                        opType,
                        validateIntegrity: true,
                        verbose,
                        detectDuplicates,
                        duplicateThreshold);

                    return exitCode;
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]Organization skipped.[/]");
                }
            }

            return hasErrors ? 2 : 0; // Return 2 if errors, 0 if no errors
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
    /// Determines export format from file extension.
    /// </summary>
    private static ExportFormat DetermineExportFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".json" => ExportFormat.Json,
            ".csv" => ExportFormat.Csv,
            ".txt" => ExportFormat.Text,
            _ => ExportFormat.Text // Default to text
        };
    }

    /// <summary>
    /// Exports metadata.json files to source audiobook folders.
    /// </summary>
    private static async Task ExportMetadataAsync(string sourcePath, bool verbose)
    {
        var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
        var metadataExtractor = Program.ServiceProvider.GetRequiredService<IMetadataExtractor>();
        var logger = Program.ServiceProvider.GetRequiredService<ILogger<PreviewCommand>>();

        AnsiConsole.MarkupLine("[yellow]Exporting metadata to source folders...[/]");
        AnsiConsole.WriteLine();

        // Scan for audiobook folders
        var folders = await AnsiConsole.Status()
            .StartAsync("[yellow]Scanning directories...[/]", async ctx =>
            {
                ctx.Spinner(Spinner.Known.Dots);
                ctx.SpinnerStyle(Style.Parse("yellow"));

                var progress = new Progress<ScanProgress>(p =>
                {
                    ctx.Status($"[yellow]Scanning:[/] {p.CurrentDirectory ?? "..."}");
                });

                return await scanner.ScanDirectoryAsync(sourcePath, progress, CancellationToken.None);
            });

        if (folders.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No audiobook folders found.[/]");
            return;
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

                        // Skip if file exists (no force option in preview)
                        if (File.Exists(metadataFilePath))
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

                        // Serialize to JSON with UTF-8 encoding
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };

                        var json = JsonSerializer.Serialize(metadataOverride, options);

                        // Write to file
                        await File.WriteAllTextAsync(metadataFilePath, json);

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
        AnsiConsole.MarkupLine("[green]✓[/] Metadata export complete:");
        AnsiConsole.MarkupLine("  [green]Exported:[/] {0}", exportedCount);
        AnsiConsole.MarkupLine("  [dim]Skipped:[/] {0} (files already exist)", skippedCount);
        if (errorCount > 0)
        {
            AnsiConsole.MarkupLine("  [red]Errors:[/] {0}", errorCount);
        }
        AnsiConsole.WriteLine();

        if (exportedCount > 0 || skippedCount > 0)
        {
            AnsiConsole.MarkupLine("[dim]Tip: Edit the metadata.json files, then run preview again to see changes[/]");
        }
    }

    /// <summary>
    /// Executes organization operation.
    /// </summary>
    private static async Task<int> ExecuteOrganizeAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        bool validateIntegrity,
        bool verbose,
        bool detectDuplicates,
        double duplicateThreshold)
    {
        try
        {
            var organizer = Program.ServiceProvider.GetRequiredService<IFileOrganizer>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<PreviewCommand>>();

            // Execute organization with progress
            OrganizationResult? result = null;

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var overallTask = ctx.AddTask("[yellow]Organizing audiobooks...[/]", maxValue: 100);
                    var currentTask = ctx.AddTask("[grey]Preparing...[/]", maxValue: 100);

                    var progress = new Progress<OrganizationProgress>(p =>
                    {
                        // Update overall progress
                        overallTask.Value = p.PercentComplete * 100;
                        overallTask.Description = $"[yellow]Organizing:[/] {p.AudiobooksCompleted}/{p.TotalAudiobooks} audiobooks";

                        // Update current operation
                        if (!string.IsNullOrEmpty(p.CurrentAudiobook))
                        {
                            currentTask.Description = $"[cyan]{p.CurrentAudiobook}[/]";
                        }

                        if (!string.IsNullOrEmpty(p.CurrentFile))
                        {
                            var fileName = Path.GetFileName(p.CurrentFile);
                            currentTask.Description = $"[dim]{fileName}[/]";
                        }

                        // Update file progress within current audiobook
                        if (p.TotalFiles > 0)
                        {
                            var filePercent = (double)p.FilesCompleted / p.TotalFiles * 100;
                            currentTask.Value = filePercent;
                        }
                    });

                    result = await organizer.OrganizeAsync(
                        sourcePath,
                        destinationPath,
                        operationType,
                        validateIntegrity,
                        detectDuplicates,
                        duplicateThreshold,
                        progress,
                        CancellationToken.None);

                    overallTask.StopTask();
                    currentTask.StopTask();
                });

            AnsiConsole.WriteLine();

            // Display results summary
            if (result != null)
            {
                AnsiConsole.Write(new Rule("[bold]Organization Results[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();

                var summaryTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn("Metric")
                    .AddColumn(new TableColumn("Value").RightAligned());

                summaryTable.AddRow("Total Audiobooks", $"[cyan]{result.TotalAudiobooks}[/]");
                summaryTable.AddRow(
                    "Successful",
                    result.SuccessfulAudiobooks > 0
                        ? $"[green]{result.SuccessfulAudiobooks}[/]"
                        : $"[dim]{result.SuccessfulAudiobooks}[/]");
                summaryTable.AddRow(
                    "Failed",
                    result.FailedAudiobooks > 0
                        ? $"[red]{result.FailedAudiobooks}[/]"
                        : $"[dim]{result.FailedAudiobooks}[/]");
                summaryTable.AddRow("Total Files", $"[cyan]{result.TotalFiles}[/]");

                AnsiConsole.Write(summaryTable);
                AnsiConsole.WriteLine();

                if (result.Success)
                {
                    AnsiConsole.MarkupLine("[green]✓ Organization completed successfully![/]");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]✗ Organization completed with errors.[/]");
                }

                return result.Success ? 0 : 1;
            }

            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Error during organization:[/] {0}", ex.Message);
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
    }
}
