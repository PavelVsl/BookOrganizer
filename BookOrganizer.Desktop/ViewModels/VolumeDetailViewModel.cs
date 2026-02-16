using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

public partial class VolumeDetailViewModel : ObservableObject
{
    private readonly VolumeNode _volume;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger _logger;

    [ObservableProperty] private string _folderPath;
    [ObservableProperty] private string _volumeName;
    [ObservableProperty] private int _fileCount;

    // Editable fields
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _series;
    [ObservableProperty] private string? _seriesNumber;
    [ObservableProperty] private string? _discNumber;
    [ObservableProperty] private string? _narrator;
    [ObservableProperty] private string? _year;

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _bookinfoContent = "";
    [ObservableProperty] private bool _hasBookinfo;

    public ObservableCollection<FolderFileInfo> Files { get; } = [];

    public VolumeDetailViewModel(VolumeNode volume, IMetadataJsonProcessor metadataProcessor, ILogger logger)
    {
        _volume = volume;
        _metadataProcessor = metadataProcessor;
        _logger = logger;
        _folderPath = volume.Path;
        _volumeName = volume.Name;
        _fileCount = volume.FileCount;

        LoadBookinfo();
        LoadFiles();
    }

    private void LoadBookinfo()
    {
        var bookinfoPath = Path.Combine(_volume.Path, "bookinfo.json");
        if (File.Exists(bookinfoPath))
        {
            HasBookinfo = true;
            try
            {
                var json = File.ReadAllText(bookinfoPath);
                BookinfoContent = json;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var meta = JsonSerializer.Deserialize<MetadataOverride>(json, options);
                if (meta != null)
                {
                    Author = meta.Author ?? "";
                    Title = meta.Title ?? "";
                    Series = meta.Series;
                    SeriesNumber = meta.SeriesNumber;
                    DiscNumber = meta.DiscNumber?.ToString();
                    Narrator = meta.Narrator;
                    Year = meta.Year?.ToString();
                    SaveStatus = meta.Source == "manual" ? "source: manual" : "has bookinfo.json";
                }
            }
            catch (Exception ex)
            {
                BookinfoContent = $"Error reading: {ex.Message}";
            }
        }
        else
        {
            SaveStatus = "no bookinfo.json";
            BookinfoContent = "Not found";
        }

        IsDirty = false;
    }

    partial void OnAuthorChanged(string value) => IsDirty = true;
    partial void OnTitleChanged(string value) => IsDirty = true;
    partial void OnSeriesChanged(string? value) => IsDirty = true;
    partial void OnSeriesNumberChanged(string? value) => IsDirty = true;
    partial void OnDiscNumberChanged(string? value) => IsDirty = true;
    partial void OnNarratorChanged(string? value) => IsDirty = true;
    partial void OnYearChanged(string? value) => IsDirty = true;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            int? yearInt = null;
            if (!string.IsNullOrWhiteSpace(Year) && int.TryParse(Year, out var parsed))
                yearInt = parsed;

            int? discNumberInt = null;
            if (!string.IsNullOrWhiteSpace(DiscNumber) && int.TryParse(DiscNumber, out var parsedDisc))
                discNumberInt = parsedDisc;

            var metadata = new MetadataOverride
            {
                Author = NullIfEmpty(Author),
                Title = NullIfEmpty(Title),
                Series = NullIfEmpty(Series),
                SeriesNumber = NullIfEmpty(SeriesNumber),
                DiscNumber = discNumberInt,
                Narrator = NullIfEmpty(Narrator),
                Year = yearInt
            };

            await _metadataProcessor.SaveMetadataAsync(_volume.Path, metadata, ct);

            IsDirty = false;
            SaveStatus = "Saved (source: manual)";

            // Reload bookinfo display
            var bookinfoPath = Path.Combine(_volume.Path, "bookinfo.json");
            if (File.Exists(bookinfoPath))
            {
                HasBookinfo = true;
                BookinfoContent = await File.ReadAllTextAsync(bookinfoPath, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metadata for {Path}", _volume.Path);
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Revert()
    {
        LoadBookinfo();
    }

    [RelayCommand]
    private void OpenInFinder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _volume.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder {Path}", _volume.Path);
        }
    }

    private void LoadFiles()
    {
        try
        {
            var entries = new DirectoryInfo(_volume.Path)
                .EnumerateFiles()
                .OrderBy(f => f.Name)
                .Select(f => new FolderFileInfo
                {
                    Name = f.Name,
                    Size = FormatFileSize(f.Length),
                    Extension = f.Extension.ToLowerInvariant()
                });

            foreach (var entry in entries)
                Files.Add(entry);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate files in {Path}", _volume.Path);
        }
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
