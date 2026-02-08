using BookOrganizer.Models;
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to reorganize an existing library based on updated metadata.
/// </summary>
public class ReorganizeCommand : Command
{
    public ReorganizeCommand() : base("reorganize", "Reorganize library based on updated metadata.json files")
    {
        var libraryOption = new Option<string?>("--library", "-l")
        {
            Description = "Library directory to reorganize (or set BOOKORGANIZER_LIBRARY env var)"
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

        var preserveDiacriticsOption = new Option<bool>("--preserve-diacritics")
        {
            Description = "Preserve Czech diacritics in folder names (UTF-8) instead of ASCII-safe names (or set BOOKORGANIZER_PRESERVE_DIACRITICS=true)",
            DefaultValueFactory = _ => string.Equals(Environment.GetEnvironmentVariable("BOOKORGANIZER_PRESERVE_DIACRITICS"), "true", StringComparison.OrdinalIgnoreCase)
        };

        Options.Add(libraryOption);
        Options.Add(noValidateOption);
        Options.Add(verboseOption);
        Options.Add(yesOption);
        Options.Add(preserveDiacriticsOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var library = parseResult.GetValue(libraryOption)
                ?? Environment.GetEnvironmentVariable("BOOKORGANIZER_LIBRARY");
            var noValidate = parseResult.GetValue(noValidateOption);
            var verbose = parseResult.GetValue(verboseOption);
            var yes = parseResult.GetValue(yesOption);
            var preserveDiacritics = parseResult.GetValue(preserveDiacriticsOption);

            if (string.IsNullOrWhiteSpace(library))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] --library is required (or set BOOKORGANIZER_LIBRARY env var)");
                return 1;
            }

            return await ExecuteAsync(
                library, !noValidate, verbose, yes, preserveDiacritics);
        });
    }

    private static async Task<int> ExecuteAsync(
        string libraryPath,
        bool validateIntegrity,
        bool verbose,
        bool autoConfirm,
        bool preserveDiacritics)
    {
        try
        {
            // Get services from DI
            var organizer = Program.ServiceProvider.GetRequiredService<IFileOrganizer>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<ReorganizeCommand>>();
            var nameDictionary = Program.ServiceProvider.GetRequiredService<INameDictionary>();

            // Load name dictionary for diacritics restoration
            await nameDictionary.LoadAsync(libraryPath);

            // Validate library path
            if (!Directory.Exists(libraryPath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Library directory does not exist: {0}", libraryPath);
                return 1;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[bold yellow]Library Reorganization[/]").RuleStyle("grey"));
            AnsiConsole.WriteLine();

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("Setting")
                .AddColumn("Value");

            table.AddRow("Library Path", libraryPath);
            table.AddRow("Operation", "[yellow]Move (within library)[/]");
            table.AddRow("Validate Integrity", validateIntegrity ? "[green]Yes[/]" : "[yellow]No[/]");
            table.AddRow("Preserve Diacritics", preserveDiacritics ? "[green]Yes[/]" : "[dim]No[/]");

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Tip: Edit bookinfo.json or metadata.json files in your library folders before running this command[/]");
            AnsiConsole.WriteLine();

            if (!autoConfirm)
            {
                if (!AnsiConsole.Confirm("Proceed with reorganization?", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[yellow]Reorganization cancelled.[/]");
                    return 0;
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[green]Auto-confirming (--yes flag)[/]");
            }

            AnsiConsole.WriteLine();

            // Execute reorganization with progress
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
                    var overallTask = ctx.AddTask("[yellow]Reorganizing library...[/]", maxValue: 100);
                    var currentTask = ctx.AddTask("[grey]Preparing...[/]", maxValue: 100);

                    var progress = new Progress<OrganizationProgress>(p =>
                    {
                        // Update overall progress
                        overallTask.Value = p.PercentComplete * 100;
                        overallTask.Description = $"[yellow]Reorganizing:[/] {p.AudiobooksCompleted}/{p.TotalAudiobooks} audiobooks";

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

                    result = await organizer.ReorganizeLibraryAsync(
                        libraryPath,
                        validateIntegrity,
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
    /// Displays the reorganization results.
    /// </summary>
    private static void DisplayResults(OrganizationResult result, bool verbose)
    {
        AnsiConsole.Write(new Rule("[bold]Reorganization Results[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Summary table
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn("Metric")
            .AddColumn(new TableColumn("Value").RightAligned());

        summaryTable.AddRow("Total Audiobooks", $"[cyan]{result.TotalAudiobooks}[/]");
        summaryTable.AddRow(
            "Reorganized",
            result.SuccessfulAudiobooks > 0
                ? $"[green]{result.SuccessfulAudiobooks}[/]"
                : $"[dim]{result.SuccessfulAudiobooks}[/]");
        summaryTable.AddRow(
            "Failed",
            result.FailedAudiobooks > 0
                ? $"[red]{result.FailedAudiobooks}[/]"
                : $"[dim]{result.FailedAudiobooks}[/]");
        summaryTable.AddRow("Total Files Moved", $"[cyan]{result.TotalFiles}[/]");
        summaryTable.AddRow("Total Size", $"[cyan]{FormatBytes(result.TotalBytesProcessed)}[/]");
        summaryTable.AddRow("Duration", $"[cyan]{FormatDuration(result.Duration)}[/]");

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Overall status
        if (result.Success)
        {
            AnsiConsole.MarkupLine("[green]✓ Reorganization completed successfully![/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[red]✗ Reorganization completed with errors.[/]");
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
