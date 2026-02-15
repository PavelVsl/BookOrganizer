using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class BatchRenameViewModel : ObservableObject
{
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger<BatchRenameViewModel> _logger;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _libraryPath = "";

    [ObservableProperty]
    private bool _isAuthorMode = true;

    [ObservableProperty]
    private ObservableCollection<NameEntry> _entries = [];

    [ObservableProperty]
    private NameEntry? _selectedEntry;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _statusText = "Load a library to see authors/series.";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterText = "";

    public ObservableCollection<NameEntry> FilteredEntries { get; } = [];

    public BatchRenameViewModel(
        IMetadataJsonProcessor metadataProcessor,
        ILogger<BatchRenameViewModel> logger,
        AppSettings settings)
    {
        _metadataProcessor = metadataProcessor;
        _logger = logger;
        _settings = settings;

        var path = settings.LibraryPath
            ?? Environment.GetEnvironmentVariable("BOOKORGANIZER_LIBRARY");
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            LibraryPath = path;
        }
    }

    partial void OnLibraryPathChanged(string value)
    {
        _settings.LibraryPath = value;
    }

    partial void OnIsAuthorModeChanged(bool value)
    {
        if (!string.IsNullOrEmpty(LibraryPath))
        {
            _ = ScanEntriesAsync(CancellationToken.None);
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedEntryChanged(NameEntry? value)
    {
        NewName = value?.Name ?? "";
    }

    [RelayCommand]
    private async Task ScanEntriesAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = "Invalid library path.";
            return;
        }

        IsLoading = true;
        StatusText = "Scanning library...";

        try
        {
            var results = await Task.Run(() => CollectEntries(ct), ct);
            Entries = new ObservableCollection<NameEntry>(results.OrderBy(e => e.Name));
            ApplyFilter();
            StatusText = $"Found {Entries.Count} {(IsAuthorMode ? "author(s)" : "series")}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan library");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ApplyRenameAsync(CancellationToken ct)
    {
        if (SelectedEntry == null || string.IsNullOrWhiteSpace(NewName))
            return;

        var oldName = SelectedEntry.Name;
        if (string.Equals(oldName, NewName, StringComparison.Ordinal))
        {
            StatusText = "New name is the same as the old name.";
            return;
        }

        IsLoading = true;
        StatusText = $"Renaming '{oldName}' to '{NewName}'...";

        try
        {
            int count;
            if (IsAuthorMode)
            {
                count = await _metadataProcessor.BatchUpdateAuthorAsync(LibraryPath, oldName, NewName, ct);
            }
            else
            {
                count = await _metadataProcessor.BatchUpdateSeriesAsync(LibraryPath, oldName, NewName, ct);
            }

            StatusText = $"Updated {count} bookinfo.json file(s). '{oldName}' -> '{NewName}'";

            // Refresh the list
            await ScanEntriesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch rename failed");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private List<NameEntry> CollectEntries(CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        foreach (var file in Directory.EnumerateFiles(LibraryPath, "bookinfo.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = File.ReadAllText(file);
                var metadata = JsonSerializer.Deserialize<MetadataOverride>(json, jsonOptions);
                if (metadata == null) continue;

                var key = IsAuthorMode ? metadata.Author : metadata.Series;
                if (string.IsNullOrWhiteSpace(key)) continue;

                counts.TryGetValue(key, out var current);
                counts[key] = current + 1;
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return counts.Select(kv => new NameEntry { Name = kv.Key, BookCount = kv.Value }).ToList();
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        var filter = FilterText?.Trim() ?? "";

        foreach (var entry in Entries)
        {
            if (string.IsNullOrEmpty(filter) ||
                entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredEntries.Add(entry);
            }
        }
    }
}

public class NameEntry
{
    public required string Name { get; init; }
    public int BookCount { get; init; }
}
