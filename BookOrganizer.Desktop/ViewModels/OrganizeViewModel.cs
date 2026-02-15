using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class OrganizeViewModel : ObservableObject
{
    private readonly IFileOrganizer _organizer;
    private readonly ILogger<OrganizeViewModel> _logger;

    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private string _destinationPath = "";
    [ObservableProperty] private string _operationType = "HardLink";
    [ObservableProperty] private bool _validateIntegrity = true;
    [ObservableProperty] private bool _detectDuplicates = true;
    [ObservableProperty] private bool _isOrganizing;
    [ObservableProperty] private string _statusText = "Configure options and click Organize.";
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _currentBook = "";
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private int _completedAudiobooks;
    [ObservableProperty] private int _totalAudiobooks;
    [ObservableProperty] private string _resultSummary = "";

    public OrganizeViewModel(IFileOrganizer organizer, ILogger<OrganizeViewModel> logger)
    {
        _organizer = organizer;
        _logger = logger;

        var envSource = Environment.GetEnvironmentVariable("BOOKORGANIZER_SOURCE");
        if (!string.IsNullOrEmpty(envSource) && Directory.Exists(envSource))
            SourcePath = envSource;

        var envDest = Environment.GetEnvironmentVariable("BOOKORGANIZER_DESTINATION");
        if (!string.IsNullOrEmpty(envDest))
            DestinationPath = envDest;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task OrganizeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestinationPath))
        {
            StatusText = "Both source and destination paths are required.";
            return;
        }

        IsOrganizing = true;
        ProgressPercent = 0;
        ResultSummary = "";
        StatusText = "Organizing...";

        try
        {
            var opType = Enum.Parse<FileOperationType>(OperationType);
            var progress = new Progress<OrganizationProgress>(p =>
            {
                ProgressPercent = p.PercentComplete * 100;
                CurrentBook = p.CurrentAudiobook ?? "";
                CurrentFile = p.CurrentFile ?? "";
                CompletedAudiobooks = p.AudiobooksCompleted;
                TotalAudiobooks = p.TotalAudiobooks;
            });

            var result = await _organizer.OrganizeAsync(
                SourcePath, DestinationPath, opType,
                validateIntegrity: ValidateIntegrity,
                detectDuplicates: DetectDuplicates,
                progress: progress,
                cancellationToken: ct);

            if (result.Success)
            {
                ResultSummary = $"Organized {result.SuccessfulAudiobooks} audiobooks ({result.TotalFiles} files, {FormatBytes(result.TotalBytesProcessed)}) in {result.Duration.TotalSeconds:0.#}s";
                StatusText = "Organization complete.";
            }
            else
            {
                ResultSummary = $"Completed with errors: {result.TotalAudiobooks} processed, {result.FailedAudiobooks} failed.";
                StatusText = "Organization completed with errors.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Organization cancelled.";
            ResultSummary = "Operation was cancelled by user.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Organization failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsOrganizing = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
