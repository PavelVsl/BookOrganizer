using BookOrganizer.Models;
using Spectre.Console;

namespace BookOrganizer.Services.Preview;

/// <summary>
/// Renders preview results to the console using Spectre.Console.
/// </summary>
public class PreviewRenderer : IPreviewRenderer
{
    public void RenderPreview(PreviewResult preview, PreviewRenderOptions? options = null)
    {
        options ??= new PreviewRenderOptions();

        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]Audiobook Organization Preview[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        // Show statistics
        if (options.ShowStatistics)
        {
            RenderStatistics(preview.Statistics);
            AnsiConsole.WriteLine();
        }

        // Show issues summary
        if (options.ShowIssues && preview.Issues.Count > 0)
        {
            RenderIssuesSummary(preview.Statistics.IssueCounts);
            AnsiConsole.WriteLine();
        }

        // Show tree view of operations
        if (options.ShowTree)
        {
            RenderOperationsTree(preview.Operations, options);
            AnsiConsole.WriteLine();
        }

        // Show detailed issues
        if (options.ShowIssues && preview.Issues.Count > 0)
        {
            RenderIssues(preview.Issues, groupBySeverity: true);
        }

        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[dim]Generated at: {preview.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC[/]");
    }

    public void RenderStatistics(PreviewStatistics statistics)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .AddColumn(new TableColumn("[bold]Statistic[/]").Centered())
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

        table.AddRow("Total Audiobooks", $"[cyan]{statistics.TotalAudiobooks:N0}[/]");
        table.AddRow("Total Files", $"[cyan]{statistics.TotalFiles:N0}[/]");
        table.AddRow("Total Size", $"[cyan]{statistics.TotalSizeFormatted}[/]");
        table.AddRow("Estimated Disk Space", $"[cyan]{statistics.EstimatedDiskSpaceFormatted}[/]");
        table.AddRow("Estimated Duration", $"[cyan]{FormatDuration(statistics.EstimatedDuration)}[/]");

        AnsiConsole.Write(new Panel(table)
            .Header("[bold yellow]Summary Statistics[/]")
            .BorderColor(Color.Yellow));
    }

    public void RenderIssues(IReadOnlyList<PreviewIssue> issues, bool groupBySeverity = true)
    {
        if (issues.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✓ No issues found![/]");
            return;
        }

        if (groupBySeverity)
        {
            // Group by severity
            var grouped = issues.GroupBy(i => i.Severity).OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                var color = GetSeverityColor(group.Key);
                var icon = GetSeverityIcon(group.Key);

                AnsiConsole.MarkupLine($"\n[bold {color}]{icon} {group.Key} ({group.Count()})[/]");

                var issueTable = new Table()
                    .Border(TableBorder.Rounded)
                    .BorderColor(Color.Grey)
                    .AddColumn("Type")
                    .AddColumn("Message")
                    .AddColumn("Path");

                foreach (var issue in group.Take(20)) // Limit to 20 per severity
                {
                    var path = issue.SourcePath ?? issue.DestinationPath ?? "";
                    if (path.Length > 60)
                    {
                        path = "..." + path.Substring(path.Length - 57);
                    }

                    issueTable.AddRow(
                        $"[dim]{issue.Type}[/]",
                        issue.Message,
                        $"[dim]{path}[/]"
                    );
                }

                if (group.Count() > 20)
                {
                    issueTable.AddRow(
                        $"[dim]...[/]",
                        $"[dim]and {group.Count() - 20} more[/]",
                        ""
                    );
                }

                AnsiConsole.Write(issueTable);
            }
        }
        else
        {
            // Flat list
            var issueTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn("Severity")
                .AddColumn("Type")
                .AddColumn("Message")
                .AddColumn("Path");

            foreach (var issue in issues)
            {
                var color = GetSeverityColor(issue.Severity);
                var icon = GetSeverityIcon(issue.Severity);
                var path = issue.SourcePath ?? issue.DestinationPath ?? "";

                if (path.Length > 50)
                {
                    path = "..." + path.Substring(path.Length - 47);
                }

                issueTable.AddRow(
                    $"[{color}]{icon} {issue.Severity}[/]",
                    $"[dim]{issue.Type}[/]",
                    issue.Message,
                    $"[dim]{path}[/]"
                );
            }

            AnsiConsole.Write(issueTable);
        }
    }

    /// <summary>
    /// Renders a summary of issues by severity.
    /// </summary>
    private void RenderIssuesSummary(IReadOnlyDictionary<IssueSeverity, int> issueCounts)
    {
        var grid = new Grid()
            .AddColumn()
            .AddColumn()
            .AddColumn();

        var hasAnyIssues = issueCounts.Values.Sum() > 0;

        if (!hasAnyIssues)
        {
            AnsiConsole.MarkupLine("[green]✓ No issues detected[/]");
            return;
        }

        var items = new List<string>();

        if (issueCounts[IssueSeverity.Error] > 0)
        {
            items.Add($"[red]✗ {issueCounts[IssueSeverity.Error]} Error(s)[/]");
        }

        if (issueCounts[IssueSeverity.Warning] > 0)
        {
            items.Add($"[yellow]⚠ {issueCounts[IssueSeverity.Warning]} Warning(s)[/]");
        }

        if (issueCounts[IssueSeverity.Info] > 0)
        {
            items.Add($"[blue]ℹ {issueCounts[IssueSeverity.Info]} Info[/]");
        }

        AnsiConsole.Write(new Panel(string.Join("  ", items))
            .Header("[bold]Issues Detected[/]")
            .BorderColor(Color.Yellow));
    }

    /// <summary>
    /// Renders the operations as a tree view.
    /// </summary>
    private void RenderOperationsTree(
        IReadOnlyList<FileOperationPreview> operations,
        PreviewRenderOptions options)
    {
        if (operations.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No operations to display[/]");
            return;
        }

        var maxToShow = options.MaxOperationsToShow ?? operations.Count;
        var operationsToShow = operations.Take(maxToShow).ToList();

        // Group by author for tree structure
        var groupedByAuthor = operationsToShow
            .GroupBy(op => op.Metadata.Author ?? "Unknown Author")
            .OrderBy(g => g.Key);

        var tree = new Tree("[bold cyan]Audiobook Library Structure[/]")
            .Style("cyan");

        foreach (var authorGroup in groupedByAuthor)
        {
            var authorNode = tree.AddNode($"[bold]{Markup.Escape(authorGroup.Key)}[/] [dim]({authorGroup.Count()} books)[/]");

            // Group by series under each author
            var seriesGroups = authorGroup
                .GroupBy(op => op.Metadata.Series ?? "Standalone")
                .OrderBy(g => g.Key);

            foreach (var seriesGroup in seriesGroups)
            {
                TreeNode seriesNode;

                if (seriesGroup.Key == "Standalone")
                {
                    // For standalone books, add directly under author
                    foreach (var op in seriesGroup.OrderBy(o => o.Metadata.Title))
                    {
                        AddBookNode(authorNode, op, options);
                    }
                }
                else
                {
                    // For series, create a series node
                    seriesNode = authorNode.AddNode($"[yellow]{Markup.Escape(seriesGroup.Key)}[/] [dim]({seriesGroup.Count()} books)[/]");

                    foreach (var op in seriesGroup.OrderBy(o =>
                        double.TryParse(o.Metadata.SeriesNumber, out var num) ? num : 0))
                    {
                        AddBookNode(seriesNode, op, options);
                    }
                }
            }
        }

        if (operations.Count > maxToShow)
        {
            tree.AddNode($"[dim]... and {operations.Count - maxToShow} more audiobooks[/]");
        }

        AnsiConsole.Write(tree);
    }

    /// <summary>
    /// Adds a book node to the tree.
    /// </summary>
    private void AddBookNode(TreeNode parentNode, FileOperationPreview op, PreviewRenderOptions options)
    {
        var title = op.Metadata.Title ?? "Unknown Title";
        var seriesInfo = "";

        if (!string.IsNullOrEmpty(op.Metadata.SeriesNumber))
        {
            seriesInfo = $" [dim]#{op.Metadata.SeriesNumber}[/]";
        }

        var sizeInfo = $"[dim]{FormatBytes(op.TotalSizeBytes)}[/]";
        var opTypeColor = GetOperationTypeColor(op.OperationType);
        var opTypeIcon = GetOperationTypeIcon(op.OperationType);
        var opTypeInfo = $"[{opTypeColor}]{opTypeIcon} {op.OperationType}[/]";

        var bookNode = parentNode.AddNode(
            $"{Markup.Escape(title)}{seriesInfo} [dim]({op.FileCount} files, {sizeInfo})[/] {opTypeInfo}"
        );

        // Show issues if any
        if (op.Issues.Count > 0)
        {
            var errorCount = op.Issues.Count(i => i.Severity == IssueSeverity.Error);
            var warningCount = op.Issues.Count(i => i.Severity == IssueSeverity.Warning);

            var issueText = new List<string>();
            if (errorCount > 0)
            {
                issueText.Add($"[red]{errorCount} error(s)[/]");
            }
            if (warningCount > 0)
            {
                issueText.Add($"[yellow]{warningCount} warning(s)[/]");
            }

            if (issueText.Count > 0)
            {
                bookNode.AddNode($"⚠ {string.Join(", ", issueText)}");
            }
        }

        // Show paths if requested
        if (options.ShowFullPaths)
        {
            var sourcePath = options.CompactMode
                ? Path.GetFileName(op.SourcePath)
                : op.SourcePath;
            var destPath = options.CompactMode
                ? Path.GetFileName(op.DestinationPath)
                : op.DestinationPath;

            bookNode.AddNode($"[dim]From:[/] {Markup.Escape(sourcePath)}");
            bookNode.AddNode($"[dim]To:[/] {Markup.Escape(destPath)}");
        }
    }

    /// <summary>
    /// Gets the color for a severity level.
    /// </summary>
    private static string GetSeverityColor(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Error => "red",
            IssueSeverity.Warning => "yellow",
            IssueSeverity.Info => "blue",
            _ => "white"
        };
    }

    /// <summary>
    /// Gets the icon for a severity level.
    /// </summary>
    private static string GetSeverityIcon(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Error => "✗",
            IssueSeverity.Warning => "⚠",
            IssueSeverity.Info => "ℹ",
            _ => "•"
        };
    }

    /// <summary>
    /// Gets the color for an operation type.
    /// </summary>
    private static string GetOperationTypeColor(FileOperationType operationType)
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
    /// Gets the icon for an operation type.
    /// </summary>
    private static string GetOperationTypeIcon(FileOperationType operationType)
    {
        return operationType switch
        {
            FileOperationType.Copy => "📋",
            FileOperationType.Move => "➡",
            FileOperationType.HardLink => "🔗",
            FileOperationType.SymbolicLink => "↪",
            _ => "•"
        };
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
}
