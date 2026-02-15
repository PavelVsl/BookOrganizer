using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Scanning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class ToolsViewModel : ObservableObject
{
    private readonly IFileOrganizer _organizer;
    private readonly IDirectoryScanner _scanner;
    private readonly IMetadataGenerator _metadataGenerator;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger<ToolsViewModel> _logger;
    private readonly AppSettings _settings;

    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private string _libraryPath = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _progressPercent;

    // Export Metadata
    [ObservableProperty] private string _exportFormat = "nfo";
    [ObservableProperty] private bool _exportForce;
    [ObservableProperty] private string _metadataSourcePath = "";

    // Verify
    public ObservableCollection<VerifyIssueItem> VerifyResults { get; } = [];

    public ToolsViewModel(
        IFileOrganizer organizer,
        IDirectoryScanner scanner,
        IMetadataGenerator metadataGenerator,
        IMetadataJsonProcessor metadataProcessor,
        ILogger<ToolsViewModel> logger,
        AppSettings settings)
    {
        _organizer = organizer;
        _scanner = scanner;
        _metadataGenerator = metadataGenerator;
        _metadataProcessor = metadataProcessor;
        _logger = logger;
        _settings = settings;

        var path = settings.LibraryPath
            ?? Environment.GetEnvironmentVariable("BOOKORGANIZER_LIBRARY");
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            LibraryPath = path;
    }

    partial void OnLibraryPathChanged(string value)
    {
        _settings.LibraryPath = value;
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ReorganizeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = "Invalid library path.";
            return;
        }

        IsBusy = true;
        ProgressPercent = 0;
        StatusText = "Reorganizing library...";

        try
        {
            var progress = new Progress<OrganizationProgress>(p =>
            {
                ProgressPercent = p.PercentComplete * 100;
            });

            var result = await _organizer.ReorganizeLibraryAsync(
                LibraryPath,
                validateIntegrity: false,
                progress: progress,
                cancellationToken: ct);

            StatusText = result.Success
                ? $"Reorganized {result.SuccessfulAudiobooks} audiobooks ({result.TotalFiles} files) in {result.Duration.TotalSeconds:0.#}s."
                : $"Reorganization completed with {result.FailedAudiobooks} failure(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Reorganization cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reorganize failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task ExportMetadataAsync(CancellationToken ct)
    {
        var sourcePath = string.IsNullOrWhiteSpace(MetadataSourcePath) ? LibraryPath : MetadataSourcePath;

        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
        {
            StatusText = "Invalid source path for metadata export.";
            return;
        }

        IsBusy = true;
        StatusText = "Exporting metadata...";

        try
        {
            var folders = await _scanner.ScanDirectoryAsync(sourcePath, ct);
            var exported = 0;
            var skipped = 0;

            foreach (var folder in folders)
            {
                ct.ThrowIfCancellationRequested();

                var result = await _metadataGenerator.GenerateMetadataFromStructureAsync(
                    folder.Path, sourcePath, ExportForce, ct);

                if (result.Success)
                    exported++;
                else
                    skipped++;
            }

            StatusText = $"Exported metadata for {exported} audiobook(s), {skipped} skipped.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Export cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export metadata failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task VerifyAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = "Invalid library path.";
            return;
        }

        IsBusy = true;
        VerifyResults.Clear();
        StatusText = "Verifying library...";

        try
        {
            var folders = await _scanner.ScanDirectoryAsync(LibraryPath, ct);
            var issues = 0;

            foreach (var folder in folders)
            {
                ct.ThrowIfCancellationRequested();

                // Check for bookinfo.json
                var bookinfoPath = Path.Combine(folder.Path, "bookinfo.json");
                if (!File.Exists(bookinfoPath))
                {
                    VerifyResults.Add(new VerifyIssueItem
                    {
                        Severity = "Warning",
                        Path = Path.GetRelativePath(LibraryPath, folder.Path),
                        Message = "Missing bookinfo.json"
                    });
                    issues++;
                }

                // Check for empty folders
                if (folder.AudioFiles.Count == 0)
                {
                    VerifyResults.Add(new VerifyIssueItem
                    {
                        Severity = "Error",
                        Path = Path.GetRelativePath(LibraryPath, folder.Path),
                        Message = "No audio files found"
                    });
                    issues++;
                }

                // Check for metadata.nfo
                var nfoPath = Path.Combine(folder.Path, "metadata.nfo");
                if (!File.Exists(nfoPath))
                {
                    VerifyResults.Add(new VerifyIssueItem
                    {
                        Severity = "Info",
                        Path = Path.GetRelativePath(LibraryPath, folder.Path),
                        Message = "Missing metadata.nfo"
                    });
                    issues++;
                }
            }

            StatusText = issues == 0
                ? $"Verified {folders.Count} audiobook(s) — no issues found."
                : $"Verified {folders.Count} audiobook(s) — {issues} issue(s) found.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Verification cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verify failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public class VerifyIssueItem
{
    public required string Severity { get; init; }
    public required string Path { get; init; }
    public required string Message { get; init; }
}
