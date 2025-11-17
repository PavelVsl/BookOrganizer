using BookOrganizer.Models;
using BookOrganizer.Services.Library;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.CommandLine;

namespace BookOrganizer.Commands;

/// <summary>
/// Command to verify library integrity and metadata consistency.
/// </summary>
public class VerifyCommand : Command
{
    public VerifyCommand() : base("verify", "Verify library integrity and metadata consistency")
    {
        var libraryOption = new Option<string>(
            aliases: ["--library", "-l"],
            description: "Library directory to verify")
        {
            IsRequired = true
        };

        var verboseOption = new Option<bool>(
            aliases: ["--verbose", "-v"],
            description: "Show detailed output");

        var checkDuplicatesOption = new Option<bool>(
            aliases: ["--check-duplicates"],
            description: "Check for potential duplicate audiobooks");

        var duplicateThresholdOption = new Option<double>(
            aliases: ["--duplicate-threshold"],
            description: "Minimum confidence for duplicate detection (0.0-1.0)",
            getDefaultValue: () => 0.7);

        AddOption(libraryOption);
        AddOption(verboseOption);
        AddOption(checkDuplicatesOption);
        AddOption(duplicateThresholdOption);

        this.SetHandler(async (context) =>
        {
            var library = context.ParseResult.GetValueForOption(libraryOption)!;
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var checkDuplicates = context.ParseResult.GetValueForOption(checkDuplicatesOption);
            var duplicateThreshold = context.ParseResult.GetValueForOption(duplicateThresholdOption);

            var exitCode = await ExecuteAsync(library, verbose, checkDuplicates, duplicateThreshold);
            context.ExitCode = exitCode;
        });
    }

    private static async Task<int> ExecuteAsync(
        string libraryPath,
        bool verbose,
        bool checkDuplicates,
        double duplicateThreshold)
    {
        try
        {
            // Get services from DI
            var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
            var metadataExtractor = Program.ServiceProvider.GetRequiredService<IMetadataExtractor>();
            var logger = Program.ServiceProvider.GetRequiredService<ILogger<VerifyCommand>>();

            // Validate library directory
            if (!Directory.Exists(libraryPath))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Library directory does not exist: {0}", libraryPath);
                return 1;
            }

            AnsiConsole.Write(new Rule("[bold yellow]Library Verification[/]").RuleStyle("grey"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Verifying library:[/] {0}", libraryPath);
            AnsiConsole.WriteLine();

            // Initialize verification state
            var totalBooks = 0;
            var booksWithIssues = 0;
            var missingFiles = 0;
            var metadataIssues = 0;
            var structureIssues = 0;
            var duplicatesFound = new List<(string book1, string book2, double confidence)>();
            var issueDetails = new List<string>();

            // Scan library
            var folders = await AnsiConsole.Status()
                .StartAsync("[yellow]Scanning library...[/]", async ctx =>
                {
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(Style.Parse("yellow"));

                    var progress = new Progress<ScanProgress>(p =>
                    {
                        ctx.Status($"[yellow]Scanning:[/] {p.CurrentDirectory ?? "..."}");
                    });

                    return await scanner.ScanDirectoryAsync(libraryPath, progress, CancellationToken.None);
                });

            totalBooks = folders.Count;

            if (totalBooks == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No audiobooks found in library.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine("[green]Found {0} audiobook(s)[/]", totalBooks);
            AnsiConsole.WriteLine();

            // Note: Duplicate detection will be performed inline during verification

            // Verify each audiobook
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
                    var task = ctx.AddTask("[yellow]Verifying audiobooks...[/]", maxValue: folders.Count);

                    foreach (var folder in folders)
                    {
                        try
                        {
                            var hasIssue = false;
                            var bookName = Path.GetFileName(folder.Path);
                            task.Description = $"[yellow]Checking:[/] {bookName}";

                            // Check 1: Verify files exist
                            var missingFilesCount = 0;
                            foreach (var file in folder.AudioFiles)
                            {
                                if (!File.Exists(file))
                                {
                                    missingFilesCount++;
                                    missingFiles++;
                                    hasIssue = true;

                                    if (verbose)
                                    {
                                        issueDetails.Add($"[red]Missing file:[/] {file}");
                                    }
                                }
                            }

                            // Check 2: Verify metadata
                            var metadata = await metadataExtractor.ExtractMetadataAsync(folder, null, CancellationToken.None);

                            if (string.IsNullOrWhiteSpace(metadata.Title))
                            {
                                metadataIssues++;
                                hasIssue = true;

                                if (verbose)
                                {
                                    issueDetails.Add($"[yellow]Missing title:[/] {folder.Path}");
                                }
                            }

                            if (string.IsNullOrWhiteSpace(metadata.Author))
                            {
                                metadataIssues++;
                                hasIssue = true;

                                if (verbose)
                                {
                                    issueDetails.Add($"[yellow]Missing author:[/] {folder.Path}");
                                }
                            }

                            // Check 3: Verify folder structure follows expected pattern
                            // Expected: {library}/{Author}/{Series?}/{Title}
                            var relativePath = Path.GetRelativePath(libraryPath, folder.Path);
                            var pathParts = relativePath.Split(Path.DirectorySeparatorChar);

                            if (pathParts.Length < 2)
                            {
                                structureIssues++;
                                hasIssue = true;

                                if (verbose)
                                {
                                    issueDetails.Add($"[yellow]Invalid structure:[/] {relativePath} (expected at least Author/Title)");
                                }
                            }

                            if (hasIssue)
                            {
                                booksWithIssues++;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to verify {Path}", folder.Path);
                            booksWithIssues++;

                            if (verbose)
                            {
                                issueDetails.Add($"[red]Error verifying:[/] {folder.Path} - {ex.Message}");
                            }
                        }

                        task.Increment(1);
                    }
                });

            AnsiConsole.WriteLine();

            // Check for duplicates if requested
            if (checkDuplicates)
            {
                AnsiConsole.MarkupLine("[yellow]Checking for duplicates using normalized metadata...[/]");

                // Note: This is a simplified duplicate check using normalized metadata
                // For full duplicate detection with confidence scoring, see PreviewCommand with --detect-duplicates
                var seen = new Dictionary<string, string>(); // normalized key -> path

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
                        var task = ctx.AddTask("[yellow]Analyzing for duplicates...[/]", maxValue: folders.Count);

                        for (int i = 0; i < folders.Count; i++)
                        {
                            var folder = folders[i];
                            var metadata = await metadataExtractor.ExtractMetadataAsync(folder, null, CancellationToken.None);

                            // Create a normalized key for duplicate detection
                            var normalizedAuthor = NormalizeForComparison(metadata.Author ?? "");
                            var normalizedTitle = NormalizeForComparison(metadata.Title);
                            var normalizedSeries = NormalizeForComparison(metadata.Series ?? "");

                            var key = $"{normalizedAuthor}|{normalizedTitle}|{normalizedSeries}";

                            if (seen.TryGetValue(key, out var existingPath))
                            {
                                // Found a potential duplicate
                                duplicatesFound.Add((folder.Path, existingPath, 1.0));
                            }
                            else
                            {
                                seen[key] = folder.Path;
                            }

                            task.Increment(1);
                        }
                    });

                AnsiConsole.WriteLine();
            }

            // Display results
            AnsiConsole.Write(new Rule("[bold]Verification Results[/]").RuleStyle("grey"));
            AnsiConsole.WriteLine();

            var summaryTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("Check")
                .AddColumn(new TableColumn("Count").RightAligned())
                .AddColumn("Status");

            summaryTable.AddRow(
                "Total Audiobooks",
                totalBooks.ToString(),
                "[cyan]—[/]");

            summaryTable.AddRow(
                "Books with Issues",
                booksWithIssues.ToString(),
                booksWithIssues > 0 ? "[yellow]⚠[/]" : "[green]✓[/]");

            summaryTable.AddRow(
                "Missing Files",
                missingFiles.ToString(),
                missingFiles > 0 ? "[red]✗[/]" : "[green]✓[/]");

            summaryTable.AddRow(
                "Metadata Issues",
                metadataIssues.ToString(),
                metadataIssues > 0 ? "[yellow]⚠[/]" : "[green]✓[/]");

            summaryTable.AddRow(
                "Structure Issues",
                structureIssues.ToString(),
                structureIssues > 0 ? "[yellow]⚠[/]" : "[green]✓[/]");

            if (checkDuplicates)
            {
                summaryTable.AddRow(
                    "Potential Duplicates",
                    duplicatesFound.Count.ToString(),
                    duplicatesFound.Count > 0 ? "[yellow]⚠[/]" : "[green]✓[/]");
            }

            AnsiConsole.Write(summaryTable);
            AnsiConsole.WriteLine();

            // Show issue details if verbose
            if (verbose && issueDetails.Count > 0)
            {
                AnsiConsole.Write(new Rule("[bold]Issue Details[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();

                foreach (var issue in issueDetails.Take(50))
                {
                    AnsiConsole.MarkupLine(issue);
                }

                if (issueDetails.Count > 50)
                {
                    AnsiConsole.MarkupLine("[dim]... and {0} more issues[/]", issueDetails.Count - 50);
                }

                AnsiConsole.WriteLine();
            }

            // Show duplicates
            if (checkDuplicates && duplicatesFound.Count > 0)
            {
                AnsiConsole.Write(new Rule("[bold]Potential Duplicates[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();

                var duplicateTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Yellow)
                    .AddColumn("Book 1")
                    .AddColumn("Book 2")
                    .AddColumn(new TableColumn("Confidence").RightAligned());

                foreach (var (book1, book2, confidence) in duplicatesFound.Take(20))
                {
                    duplicateTable.AddRow(
                        Path.GetFileName(book1) ?? book1,
                        Path.GetFileName(book2) ?? book2,
                        $"{confidence:P0}");
                }

                AnsiConsole.Write(duplicateTable);

                if (duplicatesFound.Count > 20)
                {
                    AnsiConsole.MarkupLine("[dim]... and {0} more potential duplicates[/]", duplicatesFound.Count - 20);
                }

                AnsiConsole.WriteLine();
            }

            // Overall status
            var totalIssues = missingFiles + metadataIssues + structureIssues;

            if (totalIssues == 0 && (!checkDuplicates || duplicatesFound.Count == 0))
            {
                AnsiConsole.MarkupLine("[green]✓ Library verification passed - no issues found![/]");
                return 0;
            }
            else if (missingFiles > 0)
            {
                AnsiConsole.MarkupLine("[red]✗ Library verification failed - {0} critical issue(s) found[/]", missingFiles);
                return 1;
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]⚠ Library verification completed with {0} warning(s)[/]", totalIssues + duplicatesFound.Count);
                return 0;
            }
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
    /// Normalizes a string for comparison (lowercase, trim, remove special chars).
    /// </summary>
    private static string NormalizeForComparison(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }
}
