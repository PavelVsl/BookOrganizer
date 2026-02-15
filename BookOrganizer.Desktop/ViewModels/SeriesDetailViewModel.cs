using System;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class SeriesDetailViewModel : ObservableObject
{
    private readonly SeriesNode _seriesNode;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger _logger;
    private readonly string _libraryPath;

    [ObservableProperty] private string _seriesName;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _folderPath;
    [ObservableProperty] private int _bookCount;

    private readonly string _originalName;

    public SeriesDetailViewModel(SeriesNode seriesNode, string libraryPath,
        IMetadataJsonProcessor metadataProcessor, ILogger logger)
    {
        _seriesNode = seriesNode;
        _libraryPath = libraryPath;
        _metadataProcessor = metadataProcessor;
        _logger = logger;

        _originalName = seriesNode.Name;
        _seriesName = seriesNode.Name;
        _folderPath = seriesNode.Path;
        _bookCount = seriesNode.Books.Count;
    }

    partial void OnSeriesNameChanged(string value)
    {
        IsDirty = !string.Equals(value, _originalName, StringComparison.Ordinal);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (!IsDirty || string.IsNullOrWhiteSpace(SeriesName))
            return;

        try
        {
            SaveStatus = "Saving...";

            var count = await _metadataProcessor.BatchUpdateSeriesAsync(
                _libraryPath, _originalName, SeriesName.Trim(), ct);

            // Update tree nodes
            foreach (var book in _seriesNode.Books)
                book.Series = SeriesName.Trim();

            IsDirty = false;
            SaveStatus = $"Updated {count} bookinfo.json file(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save series rename");
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Revert()
    {
        SeriesName = _originalName;
        IsDirty = false;
        SaveStatus = "";
    }
}
