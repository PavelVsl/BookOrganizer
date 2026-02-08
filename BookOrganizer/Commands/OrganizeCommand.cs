using BookOrganizer.Models;
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Text;
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
        var sourceOption = new Option<string>("--source", "-s")
        {
            Description = "Source directory containing audiobooks",
            Required = true
        };

        var destinationOption = new Option<string>("--destination", "-d", "--library", "-l")
        {
            Description = "Target library directory for organized audiobooks",
            Required = true
        };

        var operationOption = new Option<string>("--operation", "-o")
        {
            Description = "Operation type: copy, move, hardlink, symlink",
            DefaultValueFactory = _ => "copy"
        };

        var noValidateOption = new Option<bool>("--no-validate")
        {
            Description = "Skip file integrity validation (faster but risky)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output"
        };

        var yesOption = new Option<bool>("--yes", "-y")
        {
            Description = "Skip confirmation prompt (auto-confirm)"
        };

        var detectDuplicatesOption = new Option<bool>("--detect-duplicates")
        {
            Description = "Detect and merge potential duplicate audiobooks"
        };

        var duplicateThresholdOption = new Option<double>("--duplicate-threshold")
        {
            Description = "Minimum confidence for duplicate detection (0.0-1.0)",
            DefaultValueFactory = _ => 0.7
        };

        var preserveDiacriticsOption = new Option<bool>("--preserve-diacritics")
        {
            Description = "Preserve Czech diacritics in folder names (UTF-8) instead of ASCII-safe names"
        };

        Options.Add(sourceOption);
        Options.Add(destinationOption);
        Options.Add(operationOption);
        Options.Add(noValidateOption);
        Options.Add(verboseOption);
        Options.Add(yesOption);
        Options.Add(detectDuplicatesOption);
        Options.Add(duplicateThresholdOption);
        Options.Add(preserveDiacriticsOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)!;
            var destination = parseResult.GetValue(destinationOption)!;
            var operation = parseResult.GetValue(operationOption)!;
            var noValidate = parseResult.GetValue(noValidateOption);
            var verbose = parseResult.GetValue(verboseOption);
            var yes = parseResult.GetValue(yesOption);
            var detectDuplicates = parseResult.GetValue(detectDuplicatesOption);
            var duplicateThreshold = parseResult.GetValue(duplicateThresholdOption);
            var preserveDiacritics = parseResult.GetValue(preserveDiacriticsOption);

            return await ExecuteAsync(
                source, destination, operation, !noValidate, verbose, yes,
                detectDuplicates, duplicateThreshold, preserveDiacritics);
        });
    }

    private static async Task<int> ExecuteAsync(
        string sourcePath,
        string destinationPath,
        string operationType,
        bool validateIntegrity,
        bool verbose,
        bool autoConfirm,
        bool detectDuplicates,
        double duplicateThreshold,
        bool preserveDiacritics)
    {
        try
        {
            // Get services from DI
            var organizer = Program.ServiceProvider.GetRequiredService<IFileOrganizer>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<OrganizeCommand>>();
            var nameDictionary = Program.ServiceProvider.GetRequiredService<INameDictionary>();

            // Load name dictionary for diacritics restoration
            await nameDictionary.LoadAsync(destinationPath);

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
            table.AddRow("Preserve Diacritics", preserveDiacritics ? "[green]Yes[/]" : "[dim]No[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            if (!autoConfirm)
            {
                if (!AnsiConsole.Confirm("Proceed with organization?", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[yellow]Organization cancelled.[/]");
                    return 0;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Auto-confirming (--yes flag)[/]");
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

                    var organizationOptions = new Models.OrganizationOptions
                    {
                        PreserveDiacritics = preserveDiacritics
                    };

                    result = await organizer.OrganizeAsync(
                        sourcePath,
                        destinationPath,
                        opType,
                        validateIntegrity,
                        detectDuplicates,
                        duplicateThreshold,
                        progress,
                        CancellationToken.None,
                        organizationOptions);

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
