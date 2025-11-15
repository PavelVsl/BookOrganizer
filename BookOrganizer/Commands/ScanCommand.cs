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
        var sourceOption = new Option<string>(
            aliases: ["--source", "-s"],
            description: "Source directory to scan")
        {
            IsRequired = true
        };

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        AddOption(sourceOption);
        AddOption(verboseOption);

        this.SetHandler(ExecuteAsync, sourceOption, verboseOption);
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
