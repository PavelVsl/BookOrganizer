using BookOrganizer.Models;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Handles user interaction for resolving duplicate audiobooks using Spectre.Console.
/// </summary>
public class DeduplicationResolver : IDeduplicationResolver
{
    private readonly IDeduplicationCache _cache;
    private readonly ILogger<DeduplicationResolver> _logger;

    public DeduplicationResolver(
        IDeduplicationCache cache,
        ILogger<DeduplicationResolver> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Dictionary<DuplicationCandidate, DuplicationResolution>> ResolveAsync(
        List<DuplicationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var resolutions = new Dictionary<DuplicationCandidate, DuplicationResolution>();

        if (candidates.Count == 0)
            return resolutions;

        AnsiConsole.MarkupLine("[yellow]⚠[/] Found {0} potential duplicate(s)", candidates.Count);
        AnsiConsole.WriteLine();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check cache first
            var cachedResolution = await _cache.GetCachedResolutionAsync(candidate, cancellationToken);
            if (cachedResolution.HasValue)
            {
                _logger.LogDebug(
                    "Using cached resolution for '{Source}' vs '{Target}': {Resolution}",
                    candidate.SourceFolder.Path,
                    candidate.TargetFolder.Path,
                    cachedResolution.Value);

                resolutions[candidate] = cachedResolution.Value;
                continue;
            }

            // Ask user for resolution
            var resolution = await ResolveSingleAsync(candidate, cancellationToken);
            resolutions[candidate] = resolution;

            // Cache the decision
            await _cache.SaveResolutionAsync(candidate, resolution, cancellationToken);
        }

        return resolutions;
    }

    /// <inheritdoc />
    public Task<DuplicationResolution> ResolveSingleAsync(
        DuplicationCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        // Display comparison table
        DisplayDuplicateComparison(candidate);

        // Prompt user for decision
        var choices = new List<string>
        {
            $"[green]Keep both[/] - Save both versions with version suffixes",
            $"[blue]Keep source[/] - Keep '{Path.GetFileName(candidate.SourceFolder.Path)}'",
            $"[cyan]Keep target[/] - Keep '{Path.GetFileName(candidate.TargetFolder.Path)}'",
            $"[yellow]Skip[/] - Don't organize either version for now"
        };

        var recommendedChoice = GetRecommendedChoiceText(candidate.RecommendedResolution);
        var prompt = new SelectionPrompt<string>()
            .Title($"[yellow]How should this duplicate be handled?[/] (Recommended: {recommendedChoice})")
            .PageSize(10)
            .AddChoices(choices);

        var choice = AnsiConsole.Prompt(prompt);

        var resolution = choice switch
        {
            var c when c.Contains("Keep both") => DuplicationResolution.KeepBoth,
            var c when c.Contains("Keep source") => DuplicationResolution.KeepSource,
            var c when c.Contains("Keep target") => DuplicationResolution.KeepTarget,
            var c when c.Contains("Skip") => DuplicationResolution.Skip,
            _ => DuplicationResolution.Skip
        };

        AnsiConsole.WriteLine();
        return Task.FromResult(resolution);
    }

    private static void DisplayDuplicateComparison(DuplicationCandidate candidate)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn("[bold]Field[/]").Centered());
        table.AddColumn(new TableColumn($"[blue]Source[/]\n{Path.GetFileName(candidate.SourceFolder.Path)}").Centered());
        table.AddColumn(new TableColumn($"[cyan]Target[/]\n{Path.GetFileName(candidate.TargetFolder.Path)}").Centered());

        // Confidence score
        var confidenceColor = candidate.ConfidenceScore >= 0.8 ? "green" : candidate.ConfidenceScore >= 0.6 ? "yellow" : "red";
        table.AddRow(
            "[bold]Match Confidence[/]",
            $"[{confidenceColor}]{candidate.ConfidenceScore:P0}[/]",
            $"[{confidenceColor}]{candidate.ConfidenceScore:P0}[/]");

        // Metadata comparison
        AddMetadataRow(table, "Author", candidate.SourceMetadata.Author, candidate.TargetMetadata.Author);
        AddMetadataRow(table, "Title", candidate.SourceMetadata.Title, candidate.TargetMetadata.Title);

        if (!string.IsNullOrEmpty(candidate.SourceMetadata.Series) || !string.IsNullOrEmpty(candidate.TargetMetadata.Series))
        {
            AddMetadataRow(table, "Series", candidate.SourceMetadata.Series, candidate.TargetMetadata.Series);
        }

        if (!string.IsNullOrEmpty(candidate.SourceMetadata.SeriesNumber) || !string.IsNullOrEmpty(candidate.TargetMetadata.SeriesNumber))
        {
            AddMetadataRow(table, "Series #", candidate.SourceMetadata.SeriesNumber, candidate.TargetMetadata.SeriesNumber);
        }

        if (!string.IsNullOrEmpty(candidate.SourceMetadata.Narrator) || !string.IsNullOrEmpty(candidate.TargetMetadata.Narrator))
        {
            AddMetadataRow(table, "Narrator", candidate.SourceMetadata.Narrator, candidate.TargetMetadata.Narrator);
        }

        if (candidate.SourceMetadata.Year.HasValue || candidate.TargetMetadata.Year.HasValue)
        {
            AddMetadataRow(table, "Year",
                candidate.SourceMetadata.Year?.ToString() ?? "-",
                candidate.TargetMetadata.Year?.ToString() ?? "-");
        }

        // File comparison
        table.AddRow(
            "[bold]File Count[/]",
            candidate.SourceFolder.AudioFiles.Count.ToString(),
            candidate.TargetFolder.AudioFiles.Count.ToString());

        table.AddRow(
            "[bold]Total Size[/]",
            FormatBytes(candidate.SourceFolder.TotalSizeBytes),
            FormatBytes(candidate.TargetFolder.TotalSizeBytes));

        table.AddRow(
            "[bold]Path[/]",
            Markup.Escape(candidate.SourceFolder.Path),
            Markup.Escape(candidate.TargetFolder.Path));

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        // Display match reasons
        if (candidate.MatchReasons.Count > 0)
        {
            AnsiConsole.MarkupLine("[green]✓ Match reasons:[/]");
            foreach (var reason in candidate.MatchReasons)
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(reason)}");
            }
            AnsiConsole.WriteLine();
        }

        // Display differences
        if (candidate.Differences.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ Differences:[/]");
            foreach (var diff in candidate.Differences)
            {
                AnsiConsole.MarkupLine($"  • {Markup.Escape(diff)}");
            }
            AnsiConsole.WriteLine();
        }
    }

    private static void AddMetadataRow(Table table, string field, string? value1, string? value2)
    {
        var val1 = string.IsNullOrEmpty(value1) ? "[dim]-[/]" : Markup.Escape(value1);
        var val2 = string.IsNullOrEmpty(value2) ? "[dim]-[/]" : Markup.Escape(value2);

        // Highlight differences
        if (value1 != value2)
        {
            val1 = $"[yellow]{val1}[/]";
            val2 = $"[yellow]{val2}[/]";
        }

        table.AddRow($"[bold]{field}[/]", val1, val2);
    }

    private static string GetRecommendedChoiceText(DuplicationResolution resolution)
    {
        return resolution switch
        {
            DuplicationResolution.KeepBoth => "[green]Keep both[/]",
            DuplicationResolution.KeepSource => "[blue]Keep source[/]",
            DuplicationResolution.KeepTarget => "[cyan]Keep target[/]",
            DuplicationResolution.Skip => "[yellow]Skip[/]",
            _ => "[dim]Undecided[/]"
        };
    }

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
}
