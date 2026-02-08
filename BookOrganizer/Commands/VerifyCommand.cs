using BookOrganizer.Models;
using BookOrganizer.Services.Library;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Text;
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
        var libraryOption = new Option<string>("--library", "-l")
        {
            Description = "Library directory to verify",
            Required = true
        };

        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "Show detailed output"
        };

        var checkDuplicatesOption = new Option<bool>("--check-duplicates")
        {
            Description = "Check for potential duplicate audiobooks"
        };

        var duplicateThresholdOption = new Option<double>("--duplicate-threshold")
        {
            Description = "Minimum confidence for duplicate detection (0.0-1.0)",
            DefaultValueFactory = _ => 0.7
        };

        var generateMetadataOption = new Option<bool>("--generate-metadata")
        {
            Description = "Generate missing metadata files from folder structure"
        };

        var metadataFormatOption = new Option<MetadataFormat>("--metadata-format")
        {
            Description = "Metadata format for --generate-metadata: bookorganizer (default), audiobookshelf, nfo, or all",
            DefaultValueFactory = _ => MetadataFormat.BookOrganizer
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing metadata files when generating"
        };

        Options.Add(libraryOption);
        Options.Add(verboseOption);
        Options.Add(checkDuplicatesOption);
        Options.Add(duplicateThresholdOption);
        Options.Add(generateMetadataOption);
        Options.Add(metadataFormatOption);
        Options.Add(forceOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var library = parseResult.GetValue(libraryOption)!;
            var verbose = parseResult.GetValue(verboseOption);
            var checkDuplicates = parseResult.GetValue(checkDuplicatesOption);
            var duplicateThreshold = parseResult.GetValue(duplicateThresholdOption);
            var generateMetadata = parseResult.GetValue(generateMetadataOption);
            var metadataFormat = parseResult.GetValue(metadataFormatOption);
            var force = parseResult.GetValue(forceOption);

            return await ExecuteAsync(library, verbose, checkDuplicates, duplicateThreshold,
                generateMetadata, metadataFormat, force);
        });
    }

    private static async Task<int> ExecuteAsync(
        string libraryPath,
        bool verbose,
        bool checkDuplicates,
        double duplicateThreshold,
        bool generateMetadata,
        MetadataFormat metadataFormat,
        bool force)
    {
        try
        {
            // Get services from DI
            var scanner = Program.ServiceProvider.GetRequiredService<IDirectoryScanner>();
            var metadataExtractor = Program.ServiceProvider.GetRequiredService<IMetadataExtractor>();
            var metadataGenerator = Program.ServiceProvider.GetRequiredService<IMetadataGenerator>();
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

            // Check for duplicate author folders (same author name with/without diacritics)
            var textNormalizer = Program.ServiceProvider.GetRequiredService<ITextNormalizer>();
            var duplicateAuthorGroups = CheckDuplicateAuthorFolders(libraryPath, textNormalizer);
            if (duplicateAuthorGroups.Count > 0)
            {
                AnsiConsole.Write(new Rule("[bold yellow]Duplicate Author Folders[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Found {0} author name(s) with multiple folders (diacritics variants):[/]",
                    duplicateAuthorGroups.Count);
                AnsiConsole.WriteLine();

                foreach (var group in duplicateAuthorGroups)
                {
                    AnsiConsole.MarkupLine("[yellow]  '{0}' has {1} folders:[/]", group.Key, group.Value.Count);
                    foreach (var folder in group.Value)
                    {
                        AnsiConsole.MarkupLine("    - {0}", Path.GetFileName(folder));
                    }
                }
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Run 'reorganize' to merge these into single ASCII-named folders.[/]");
                AnsiConsole.WriteLine();
            }

            // Generate metadata.json files if requested
            var metadataGenerated = 0;
            var metadataSkipped = 0;
            var metadataErrors = 0;

            if (generateMetadata)
            {
                AnsiConsole.Write(new Rule("[bold yellow]Metadata Generation[/]").RuleStyle("grey"));
                AnsiConsole.WriteLine();

                // Build a set of audiobook folder paths for quick lookup
                var audiobookPaths = new HashSet<string>(
                    folders.Select(f => f.Path),
                    StringComparer.OrdinalIgnoreCase);

                // Collect all folders at all levels (author, series, book)
                var allFolders = new List<string>();

                // Add all author folders
                var authorFolders = Directory.GetDirectories(libraryPath);
                allFolders.AddRange(authorFolders);

                // Add all series folders and book folders
                foreach (var authorFolder in authorFolders)
                {
                    var subFolders = Directory.GetDirectories(authorFolder);
                    allFolders.AddRange(subFolders);

                    // Also add any third-level folders (books under series)
                    foreach (var seriesFolder in subFolders)
                    {
                        var bookFolders = Directory.GetDirectories(seriesFolder);
                        allFolders.AddRange(bookFolders);
                    }
                }

                // Get formatters for writing metadata files
                var formatters = Program.ServiceProvider.GetRequiredService<IEnumerable<IMetadataFormatter>>();
                var activeFormatters = metadataFormat switch
                {
                    MetadataFormat.BookOrganizer => formatters.Where(f => f is BookOrganizerFormatter).ToList(),
                    MetadataFormat.Audiobookshelf => formatters.Where(f => f is AudiobookshelfFormatter).ToList(),
                    MetadataFormat.Nfo => formatters.Where(f => f is NfoFormatter).ToList(),
                    MetadataFormat.All => formatters.ToList(),
                    _ => formatters.Where(f => f is BookOrganizerFormatter).ToList()
                };

                var formatDescription = metadataFormat == MetadataFormat.All
                    ? "all formats"
                    : metadataFormat.ToString().ToLowerInvariant();
                AnsiConsole.MarkupLine("[dim]Output format:[/] {0}", formatDescription);
                AnsiConsole.WriteLine();

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
                        var task = ctx.AddTask("[yellow]Generating metadata files...[/]", maxValue: allFolders.Count);

                        foreach (var folderPath in allFolders)
                        {
                            try
                            {
                                var folderName = Path.GetFileName(folderPath);
                                task.Description = $"[yellow]Processing:[/] {folderName}";

                                // For audiobook folders (with audio files), use full MetadataExtractor
                                // to get narrator, year, genre from MP3 tags
                                var matchingFolder = folders.FirstOrDefault(f =>
                                    string.Equals(f.Path, folderPath, StringComparison.OrdinalIgnoreCase));

                                if (matchingFolder != null)
                                {
                                    // Delete existing metadata files first to prevent circular dependency
                                    // (extractor reads bookinfo.json which was generated by us)
                                    foreach (var formatter in activeFormatters)
                                    {
                                        var existingFile = Path.Combine(folderPath, formatter.FileName);
                                        if (File.Exists(existingFile))
                                            File.Delete(existingFile);
                                    }
                                    // Also remove metadata.json to prevent Audiobookshelf format interference
                                    var metadataJsonPath = Path.Combine(folderPath, "metadata.json");
                                    if (File.Exists(metadataJsonPath))
                                        File.Delete(metadataJsonPath);

                                    var metadata = await metadataExtractor.ExtractMetadataAsync(
                                        matchingFolder, null, CancellationToken.None);

                                    var allSkipped = true;
                                    foreach (var formatter in activeFormatters)
                                    {
                                        var metadataFilePath = Path.Combine(folderPath, formatter.FileName);
                                        if (File.Exists(metadataFilePath) && !force)
                                            continue;

                                        allSkipped = false;
                                        var content = await formatter.FormatAsync(metadata, CancellationToken.None);
                                        await File.WriteAllTextAsync(metadataFilePath, content, CancellationToken.None);

                                        metadataGenerated++;
                                        if (verbose)
                                            AnsiConsole.MarkupLine("[green]Generated:[/] {0}", metadataFilePath);
                                    }

                                    if (allSkipped)
                                        metadataSkipped++;
                                }
                                else
                                {
                                    // For non-audiobook folders (author/series), use folder structure
                                    var result = await metadataGenerator.GenerateMetadataFromStructureAsync(
                                        folderPath, libraryPath, metadataFormat, force, CancellationToken.None);

                                    if (result.Success)
                                    {
                                        if (result.Skipped)
                                            metadataSkipped++;
                                        else
                                        {
                                            metadataGenerated++;
                                            if (verbose)
                                                AnsiConsole.MarkupLine("[green]Generated:[/] {0}", result.FilePath);
                                        }
                                    }
                                    else
                                    {
                                        metadataErrors++;
                                        if (verbose)
                                            AnsiConsole.MarkupLine("[yellow]Warning:[/] {0} - {1}",
                                                folderPath, result.ErrorMessage);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex, "Failed to generate metadata for {Path}", folderPath);
                                metadataErrors++;
                            }

                            task.Increment(1);
                        }
                    });

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]Metadata generation complete:[/]");
                AnsiConsole.MarkupLine("  Generated: {0}", metadataGenerated);
                AnsiConsole.MarkupLine("  Skipped: {0}", metadataSkipped);
                AnsiConsole.MarkupLine("  Errors: {0}", metadataErrors);
                AnsiConsole.WriteLine();
            }

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

            summaryTable.AddRow(
                "Duplicate Author Folders",
                duplicateAuthorGroups.Count.ToString(),
                duplicateAuthorGroups.Count > 0 ? "[yellow]⚠[/]" : "[green]✓[/]");

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
    /// Checks for author folders that are diacritics variants of the same name.
    /// Groups top-level library folders by normalized comparison key.
    /// </summary>
    private static Dictionary<string, List<string>> CheckDuplicateAuthorFolders(
        string libraryPath, ITextNormalizer textNormalizer)
    {
        var authorFolders = Directory.GetDirectories(libraryPath);
        var groups = new Dictionary<string, List<string>>();

        foreach (var folder in authorFolders)
        {
            var folderName = Path.GetFileName(folder);
            var normalized = textNormalizer.NormalizeForComparison(folderName);

            if (!groups.ContainsKey(normalized))
                groups[normalized] = [];

            groups[normalized].Add(folder);
        }

        // Return only groups with more than one folder
        return groups
            .Where(g => g.Value.Count > 1)
            .ToDictionary(g => g.Key, g => g.Value);
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
