using BookOrganizer.Services.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to scan directories for audiobook folders.
/// </summary>
public class ScanCommand : Command
{
    public ScanCommand() : base("scan", "Scan directories for audiobook folders")
    {
        var sourceOption = new Option<string?>("--source", "-s")
        {
            Description = "Source directory to scan (or set BOOKORGANIZER_SOURCE env var)"
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output"
        };

        Options.Add(sourceOption);
        Options.Add(verboseOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)
                ?? Environment.GetEnvironmentVariable("BOOKORGANIZER_SOURCE");
            var verbose = parseResult.GetValue(verboseOption);

            if (string.IsNullOrWhiteSpace(source))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] --source is required (or set BOOKORGANIZER_SOURCE env var)");
                return 1;
            }

            return await ExecuteAsync(source, verbose);
        });
    }

    private static async Task<int> ExecuteAsync(string sourcePath, bool verbose)
    {
        try
        {
            // Get services from DI (will be injected via handler setup)
            var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<ScanCommand>>();

            if (!Directory.Exists(sourcePath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Source directory does not exist: {0}", sourcePath);
                return 1;
            }

            AnsiConsole.MarkupLine("[green]Scanning directory:[/] {0}", sourcePath);
            AnsiConsole.WriteLine();

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
                        if (!task.IsIndeterminate)
                        {
                            task.Value = p.DirectoriesScanned;
                        }
                    });

                    var result = await scanner.ScanDirectoryAsync(sourcePath, progress, CancellationToken.None);

                    task.StopTask();
                    return result;
                });

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Scan complete![/]");
            AnsiConsole.WriteLine();

            // Display results in a table
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[yellow]Path[/]");
            table.AddColumn(new TableColumn("[yellow]Files[/]").RightAligned());
            table.AddColumn(new TableColumn("[yellow]Size (MB)[/]").RightAligned());

            foreach (var folder in folders)
            {
                var sizeMB = folder.TotalSizeBytes / 1024.0 / 1024.0;
                table.AddRow(
                    folder.Path,
                    folder.FileCount.ToString(),
                    $"{sizeMB:F2}");
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine(
                "[green]Found {0} audiobook folder(s)[/] with [cyan]{1} total file(s)[/]",
                folders.Count,
                folders.Sum(f => f.FileCount));

            return 0;
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
