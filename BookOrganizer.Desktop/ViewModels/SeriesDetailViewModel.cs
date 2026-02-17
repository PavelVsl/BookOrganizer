using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Desktop.Services;
using BookOrganizer.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class SeriesDetailViewModel : ObservableObject
{
    private readonly SeriesNode _seriesNode;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly PublishQueueService _publishQueue;
    private readonly AppSettings _settings;
    private readonly ILogger _logger;
    private readonly string _libraryPath;

    [ObservableProperty] private string _seriesName;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _folderPath;
    [ObservableProperty] private int _bookCount;
    [ObservableProperty] private int _unpublishedCount;
    [ObservableProperty] private string _publishStatus = "";
    [ObservableProperty] private bool _canPublish;

    private readonly string _originalName;

    public SeriesDetailViewModel(SeriesNode seriesNode, string libraryPath,
        IMetadataJsonProcessor metadataProcessor, PublishQueueService publishQueue,
        AppSettings settings, ILogger logger)
    {
        _seriesNode = seriesNode;
        _libraryPath = libraryPath;
        _metadataProcessor = metadataProcessor;
        _publishQueue = publishQueue;
        _settings = settings;
        _logger = logger;

        _originalName = seriesNode.Name;
        _seriesName = seriesNode.Name;
        _folderPath = seriesNode.Path;
        _bookCount = seriesNode.Books.Count;
        _unpublishedCount = seriesNode.Books.Count(b => !b.IsPublished);
        _canPublish = _unpublishedCount > 0 && !string.IsNullOrWhiteSpace(settings.AbsLibraryFolder);
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

    [RelayCommand]
    private void PublishAll()
    {
        if (string.IsNullOrWhiteSpace(_settings.AbsLibraryFolder))
        {
            PublishStatus = "ABS library folder not configured.";
            return;
        }

        var unpublished = _seriesNode.Books.Where(b => !b.IsPublished).ToList();

        if (unpublished.Count == 0)
        {
            PublishStatus = "All books already published.";
            return;
        }

        var items = unpublished.Select(b => (
            b,
            new BookMetadata
            {
                Title = b.Title,
                Author = b.Author,
                Series = b.Series,
                SeriesNumber = b.SeriesNumber,
                Narrator = b.Narrator,
                Year = b.Year,
                DiscNumber = b.DiscNumber,
                Genre = b.Genre,
                Description = b.Description,
                Language = b.Language,
                Confidence = b.Confidence,
                Source = "GUI"
            }
        )).ToList();

        _publishQueue.EnqueueRange(items, _settings.AbsLibraryFolder);
        CanPublish = false;
        PublishStatus = $"Queued {unpublished.Count} book(s) for publishing.";
    }
}
