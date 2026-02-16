using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class VolumeDetailViewModel : ObservableObject
{
    private readonly VolumeNode _volume;
    private readonly ILogger _logger;

    [ObservableProperty] private string _folderPath;
    [ObservableProperty] private string _volumeName;
    [ObservableProperty] private int _fileCount;

    public ObservableCollection<FolderFileInfo> Files { get; } = [];

    public VolumeDetailViewModel(VolumeNode volume, ILogger logger)
    {
        _volume = volume;
        _logger = logger;
        _folderPath = volume.Path;
        _volumeName = volume.Name;
        _fileCount = volume.FileCount;

        LoadFiles();
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
}
