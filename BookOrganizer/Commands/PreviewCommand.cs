using BookOrganizer.Models;
using BookOrganizer.Services.Preview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

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
            aliases: ["--destination", "-d"],
            description: "Destination directory for organized library")
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

            var exitCode = await ExecuteAsync(
                source, destination, operation, export,
                authorFilter, seriesFilter, maxItemsFilter,
                compactMode, noTreeMode, verboseMode,
                detectDuplicates, duplicateThreshold, rebuildCache);

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
        bool rebuildCache)
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
}
