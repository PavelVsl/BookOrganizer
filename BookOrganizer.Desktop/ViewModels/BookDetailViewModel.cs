using System;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class BookDetailViewModel : ObservableObject
{
    private readonly BookNode _bookNode;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger _logger;

    // Editable fields
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _series;
    [ObservableProperty] private string? _seriesNumber;
    [ObservableProperty] private string? _narrator;
    [ObservableProperty] private string? _year;
    [ObservableProperty] private string? _genre;
    [ObservableProperty] private string? _publisher;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _language;

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _folderPath = "";
    [ObservableProperty] private int _audioFileCount;

    public BookDetailViewModel(BookNode bookNode, IMetadataJsonProcessor metadataProcessor, ILogger logger)
    {
        _bookNode = bookNode;
        _metadataProcessor = metadataProcessor;
        _logger = logger;

        FolderPath = bookNode.Path;
        AudioFileCount = bookNode.AudioFileCount;

        // Load current values
        LoadFromBookNode();
    }

    private void LoadFromBookNode()
    {
        Author = _bookNode.Author;
        Title = _bookNode.Title;
        Series = _bookNode.Series;
        SeriesNumber = _bookNode.SeriesNumber;
        Narrator = _bookNode.Narrator;
        Year = _bookNode.Year?.ToString();
        Genre = _bookNode.Genre;
        Publisher = _bookNode.Publisher;
        Description = _bookNode.Description;
        Language = _bookNode.Language;
        IsDirty = false;
        SaveStatus = _bookNode.IsManual ? "source: manual" : (_bookNode.HasBookinfo ? "has bookinfo.json" : "no bookinfo.json");
    }

    partial void OnAuthorChanged(string value) => IsDirty = true;
    partial void OnTitleChanged(string value) => IsDirty = true;
    partial void OnSeriesChanged(string? value) => IsDirty = true;
    partial void OnSeriesNumberChanged(string? value) => IsDirty = true;
    partial void OnNarratorChanged(string? value) => IsDirty = true;
    partial void OnYearChanged(string? value) => IsDirty = true;
    partial void OnGenreChanged(string? value) => IsDirty = true;
    partial void OnPublisherChanged(string? value) => IsDirty = true;
    partial void OnDescriptionChanged(string? value) => IsDirty = true;
    partial void OnLanguageChanged(string? value) => IsDirty = true;

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            int? yearInt = null;
            if (!string.IsNullOrWhiteSpace(Year) && int.TryParse(Year, out var parsed))
                yearInt = parsed;

            var metadata = new MetadataOverride
            {
                Author = NullIfEmpty(Author),
                Title = NullIfEmpty(Title),
                Series = NullIfEmpty(Series),
                SeriesNumber = NullIfEmpty(SeriesNumber),
                Narrator = NullIfEmpty(Narrator),
                Year = yearInt,
                Genre = NullIfEmpty(Genre),
                Publisher = NullIfEmpty(Publisher),
                Description = NullIfEmpty(Description),
                Language = NullIfEmpty(Language)
            };

            await _metadataProcessor.SaveMetadataAsync(_bookNode.Path, metadata, ct);

            // Update the tree node to reflect changes
            _bookNode.Author = Author;
            _bookNode.Title = Title;
            _bookNode.Series = Series;
            _bookNode.SeriesNumber = SeriesNumber;
            _bookNode.Narrator = Narrator;
            _bookNode.Year = yearInt;
            _bookNode.Genre = Genre;
            _bookNode.Publisher = Publisher;
            _bookNode.Description = Description;
            _bookNode.Language = Language;

            IsDirty = false;
            SaveStatus = "Saved (source: manual)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save metadata for {Path}", _bookNode.Path);
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Revert()
    {
        LoadFromBookNode();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
