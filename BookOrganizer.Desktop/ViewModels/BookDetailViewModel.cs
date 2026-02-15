using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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

    // Metadata source display
    [ObservableProperty] private string _mp3TagsSummary = "";
    [ObservableProperty] private string _bookinfoContent = "";
    [ObservableProperty] private string _nfoContent = "";
    [ObservableProperty] private bool _hasMp3Tags;
    [ObservableProperty] private bool _hasBookinfo;
    [ObservableProperty] private bool _hasNfo;

    public BookDetailViewModel(BookNode bookNode, IMetadataJsonProcessor metadataProcessor, ILogger logger)
    {
        _bookNode = bookNode;
        _metadataProcessor = metadataProcessor;
        _logger = logger;

        FolderPath = bookNode.Path;
        AudioFileCount = bookNode.AudioFileCount;

        // Load current values
        LoadFromBookNode();

        // Load metadata sources lazily
        LoadMetadataSources();
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

    private void LoadMetadataSources()
    {
        var folderPath = _bookNode.Path;

        // mp3tags.json
        var mp3TagsPath = Path.Combine(folderPath, "mp3tags.json");
        if (File.Exists(mp3TagsPath))
        {
            HasMp3Tags = true;
            try
            {
                var json = File.ReadAllText(mp3TagsPath);
                var cache = JsonSerializer.Deserialize<Mp3TagsCache>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (cache?.Files != null)
                {
                    Mp3TagsSummary = BuildMp3TagsSummary(cache);
                }
            }
            catch (Exception ex)
            {
                Mp3TagsSummary = $"Error reading: {ex.Message}";
            }
        }
        else
        {
            Mp3TagsSummary = "Not found";
        }

        // bookinfo.json
        var bookinfoPath = Path.Combine(folderPath, "bookinfo.json");
        if (File.Exists(bookinfoPath))
        {
            HasBookinfo = true;
            try
            {
                BookinfoContent = File.ReadAllText(bookinfoPath);
            }
            catch (Exception ex)
            {
                BookinfoContent = $"Error reading: {ex.Message}";
            }
        }
        else
        {
            BookinfoContent = "Not found";
        }

        // metadata.nfo
        var nfoPath = Path.Combine(folderPath, "metadata.nfo");
        if (File.Exists(nfoPath))
        {
            HasNfo = true;
            try
            {
                NfoContent = File.ReadAllText(nfoPath);
            }
            catch (Exception ex)
            {
                NfoContent = $"Error reading: {ex.Message}";
            }
        }
        else
        {
            NfoContent = "Not found";
        }
    }

    private static string BuildMp3TagsSummary(Mp3TagsCache cache)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Scanned: {cache.ScannedAtUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"Files: {cache.Files.Count}");
        sb.AppendLine();

        // Most common values per field
        var artists = MostCommon(cache.Files.Select(f => f.Tags.Artist));
        var albums = MostCommon(cache.Files.Select(f => f.Tags.Album));
        var composers = MostCommon(cache.Files.Select(f => f.Tags.Composer));
        var albumArtists = MostCommon(cache.Files.Select(f => f.Tags.AlbumArtist));
        var genres = MostCommon(cache.Files.Select(f => f.Tags.Genre));
        var years = cache.Files.Where(f => f.Tags.Year > 0).Select(f => f.Tags.Year).Distinct().ToList();

        if (artists != null) sb.AppendLine($"Artist (narrator): {artists}");
        if (albumArtists != null) sb.AppendLine($"Album Artist: {albumArtists}");
        if (composers != null) sb.AppendLine($"Composer (author): {composers}");
        if (albums != null) sb.AppendLine($"Album (title): {albums}");
        if (genres != null) sb.AppendLine($"Genre: {genres}");
        if (years.Count > 0) sb.AppendLine($"Year: {string.Join(", ", years)}");

        var totalDuration = TimeSpan.FromSeconds(cache.Files.Sum(f => f.Tags.DurationSeconds));
        sb.AppendLine($"Total duration: {totalDuration:hh\\:mm\\:ss}");

        return sb.ToString().TrimEnd();
    }

    private static string? MostCommon(IEnumerable<string?> values)
    {
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
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
