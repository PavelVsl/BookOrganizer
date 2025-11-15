using BookOrganizer.Models;
using BookOrganizer.Services.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to organize audiobooks by executing file operations.
/// </summary>
public class OrganizeCommand : Command
{
    public OrganizeCommand() : base("organize", "Organize audiobooks to target directory")
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

        var noValidateOption = new Option<bool>(
            aliases: ["--no-validate"],
            description: "Skip file integrity validation (faster but risky)");

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        AddOption(sourceOption);
        AddOption(destinationOption);
        AddOption(operationOption);
        AddOption(noValidateOption);
        AddOption(verboseOption);

        this.SetHandler(async (context) =>
        {
            var source = context.ParseResult.GetValueForOption(sourceOption)!;
            var destination = context.ParseResult.GetValueForOption(destinationOption)!;
            var operation = context.ParseResult.GetValueForOption(operationOption)!;
            var noValidate = context.ParseResult.GetValueForOption(noValidateOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            var exitCode = await ExecuteAsync(
                source, destination, operation, !noValidate, verbose);

            context.ExitCode = exitCode;
        });
    }

    private static async Task<int> ExecuteAsync(
        string sourcePath,
        string destinationPath,
        string operationType,
        bool validateIntegrity,
        bool verbose)
    {
        try
        {
            // Get services from DI
            var organizer = Program.ServiceProvider.GetRequiredService<IFileOrganizer>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<OrganizeCommand>>();

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

            // Confirm with user
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold yellow]Audiobook Organization[/]").RuleStyle("grey"));
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("Setting")
                .AddColumn("Value");

            table.AddRow("Source", sourcePath);
            table.AddRow("Destination", destinationPath);
            table.AddRow("Operation", $"[{GetOperationColor(opType)}]{opType}[/]");
            table.AddRow("Validate Integrity", validateIntegrity ? "[green]Yes[/]" : "[yellow]No[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            if (!AnsiConsole.Confirm("Proceed with organization?", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[yellow]Organization cancelled.[/]");
                return 0;
            }

            AnsiConsole.WriteLine();

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
                        opType,
                        validateIntegrity,
                        progress,
                        CancellationToken.None);

                    overallTask.StopTask();
                    currentTask.StopTask();
                });

            AnsiConsole.WriteLine();

            // Display results
            if (result != null)
            {
                DisplayResults(result, verbose);
                return result.Success ? 0 : 1;
            }

            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Error:[/] {0}", ex.Message);
            if (verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
    }

    /// <summary>
    /// Displays the organization results.
    /// </summary>
    private static void DisplayResults(OrganizationResult result, bool verbose)
    {
        AnsiConsole.Write(new Rule("[bold]Organization Results[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Summary table
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
        summaryTable.AddRow("Total Size", $"[cyan]{FormatBytes(result.TotalBytesProcessed)}[/]");
        summaryTable.AddRow("Duration", $"[cyan]{FormatDuration(result.Duration)}[/]");

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Overall status
        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]✓ Organization completed successfully![/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]✗ Organization completed with errors.[/]");
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                AnsiConsole.MarkupLine("[red]  {0}[/]", result.ErrorMessage);
            }
        }

        // Show failed audiobooks if verbose
        if (verbose && result.FailedAudiobooks > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Failed Audiobooks:[/]");

            var failedTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Red)
                .AddColumn("Author")
                .AddColumn("Title")
                .AddColumn("Error");

            foreach (var audiobook in result.AudiobookResults.Where(r => !r.Success))
            {
                failedTable.AddRow(
                    audiobook.Metadata.Author ?? "Unknown",
                    audiobook.Metadata.Title,
                    audiobook.ErrorMessage ?? "Unknown error");
            }

            AnsiConsole.Write(failedTable);
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Gets the color for an operation type.
    /// </summary>
    private static string GetOperationColor(FileOperationType operationType)
    {
        return operationType switch
        {
            FileOperationType.Copy => "green",
            FileOperationType.Move => "yellow",
            FileOperationType.HardLink => "cyan",
            FileOperationType.SymbolicLink => "magenta",
            _ => "white"
        };
    }

    /// <summary>
    /// Formats bytes into human-readable string.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Formats a duration into a readable string.
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return $"{duration.TotalSeconds:0.#}s";
        }
        else if (duration.TotalMinutes < 60)
        {
            return $"{duration.TotalMinutes:0.#}m";
        }
        else
        {
            return $"{duration.TotalHours:0.#}h";
        }
    }
}
