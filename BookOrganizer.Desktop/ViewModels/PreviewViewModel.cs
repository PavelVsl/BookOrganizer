using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Preview;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class PreviewViewModel : ObservableObject
{
    private readonly IPreviewGenerator _previewGenerator;
    private readonly ILogger<PreviewViewModel> _logger;

    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private string _destinationPath = "";
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private string _statusText = "Enter source and destination, then click Generate Preview.";
    [ObservableProperty] private string _operationType = "HardLink";
    [ObservableProperty] private bool _detectDuplicates = true;
    [ObservableProperty] private int _totalAudiobooks;
    [ObservableProperty] private int _totalFiles;
    [ObservableProperty] private string _totalSize = "";
    [ObservableProperty] private int _issueCount;

    public ObservableCollection<PreviewItem> Operations { get; } = [];
    public ObservableCollection<PreviewIssueItem> Issues { get; } = [];

    public PreviewViewModel(IPreviewGenerator previewGenerator, ILogger<PreviewViewModel> logger)
    {
        _previewGenerator = previewGenerator;
        _logger = logger;

        var envSource = Environment.GetEnvironmentVariable("BOOKORGANIZER_SOURCE");
        if (!string.IsNullOrEmpty(envSource) && Directory.Exists(envSource))
            SourcePath = envSource;

        var envDest = Environment.GetEnvironmentVariable("BOOKORGANIZER_DESTINATION");
        if (!string.IsNullOrEmpty(envDest))
            DestinationPath = envDest;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GenerateAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestinationPath))
        {
            StatusText = "Both source and destination paths are required.";
            return;
        }

        IsGenerating = true;
        Operations.Clear();
        Issues.Clear();
        StatusText = "Generating preview...";

        try
        {
            var opType = Enum.Parse<FileOperationType>(OperationType);
            var preview = await _previewGenerator.GeneratePreviewAsync(
                SourcePath, DestinationPath, opType,
                detectDuplicates: DetectDuplicates,
                cancellationToken: ct);

            TotalAudiobooks = preview.Statistics.TotalAudiobooks;
            TotalFiles = preview.Statistics.TotalFiles;
            TotalSize = preview.Statistics.TotalSizeFormatted;
            IssueCount = preview.Issues.Count;

            foreach (var op in preview.Operations)
            {
                Operations.Add(new PreviewItem
                {
                    Author = op.NormalizedAuthor ?? "Unknown",
                    Title = op.Metadata.Title ?? "Unknown",
                    Series = op.NormalizedSeries,
                    SourcePath = op.SourcePath,
                    DestinationPath = op.DestinationPath,
                    FileCount = op.FileCount,
                    SizeMb = op.TotalSizeBytes / (1024.0 * 1024.0),
                    IssueCount = op.Issues.Count
                });
            }

            foreach (var issue in preview.Issues)
            {
                Issues.Add(new PreviewIssueItem
                {
                    Severity = issue.Severity.ToString(),
                    Type = issue.Type.ToString(),
                    Message = issue.Message,
                    Path = issue.SourcePath ?? issue.DestinationPath ?? ""
                });
            }

            StatusText = $"Preview: {TotalAudiobooks} audiobooks, {TotalFiles} files ({TotalSize}), {IssueCount} issue(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Preview cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview generation failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }
}

public class PreviewItem
{
    public required string Author { get; init; }
    public required string Title { get; init; }
    public string? Series { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public int FileCount { get; init; }
    public double SizeMb { get; init; }
    public int IssueCount { get; init; }
}

public class PreviewIssueItem
{
    public required string Severity { get; init; }
    public required string Type { get; init; }
    public required string Message { get; init; }
    public required string Path { get; init; }
}
