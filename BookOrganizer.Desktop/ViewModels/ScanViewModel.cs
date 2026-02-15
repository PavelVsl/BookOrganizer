using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class ScanViewModel : ObservableObject
{
    private readonly IDirectoryScanner _scanner;
    private readonly ILogger<ScanViewModel> _logger;

    [ObservableProperty] private string _sourcePath = "";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Enter source path and click Scan.";
    [ObservableProperty] private int _directoriesScanned;
    [ObservableProperty] private int _audiobooksFound;
    [ObservableProperty] private int _audioFilesFound;
    [ObservableProperty] private string _currentDirectory = "";

    public ObservableCollection<ScanResultItem> Results { get; } = [];

    public ScanViewModel(IDirectoryScanner scanner, ILogger<ScanViewModel> logger)
    {
        _scanner = scanner;
        _logger = logger;

        var envPath = Environment.GetEnvironmentVariable("BOOKORGANIZER_SOURCE");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            SourcePath = envPath;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ScanAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !Directory.Exists(SourcePath))
        {
            StatusText = "Invalid source path.";
            return;
        }

        IsScanning = true;
        Results.Clear();
        DirectoriesScanned = 0;
        AudiobooksFound = 0;
        AudioFilesFound = 0;
        StatusText = "Scanning...";

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                DirectoriesScanned = p.DirectoriesScanned;
                AudiobooksFound = p.AudiobookFoldersFound;
                AudioFilesFound = p.AudioFilesFound;
                CurrentDirectory = p.CurrentDirectory ?? "";
            });

            var folders = await _scanner.ScanDirectoryAsync(SourcePath, progress, ct);

            foreach (var folder in folders.OrderBy(f => f.Path))
            {
                Results.Add(new ScanResultItem
                {
                    Path = Path.GetRelativePath(SourcePath, folder.Path),
                    FileCount = folder.AudioFiles.Count,
                    SizeMb = folder.TotalSizeBytes / (1024.0 * 1024.0)
                });
            }

            StatusText = $"Found {folders.Count} audiobook folder(s) with {folders.Sum(f => f.AudioFiles.Count)} files.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
}

public class ScanResultItem
{
    public required string Path { get; init; }
    public int FileCount { get; init; }
    public double SizeMb { get; init; }
}
