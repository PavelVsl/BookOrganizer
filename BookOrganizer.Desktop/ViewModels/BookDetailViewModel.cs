using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class BookDetailViewModel : ObservableObject
{
    private readonly BookNode _bookNode;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly IFileOrganizer _fileOrganizer;
    private readonly IPathGenerator _pathGenerator;
    private readonly string _libraryPath;
    private readonly Func<string?, Task> _reloadCallback;
    private readonly ILogger _logger;

    // Editable fields
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _series;
    [ObservableProperty] private string? _seriesNumber;
    [ObservableProperty] private string? _narrator;
    [ObservableProperty] private string? _year;
    [ObservableProperty] private string? _genre;
    [ObservableProperty] private string? _discNumber;
    [ObservableProperty] private string? _publisher;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _language;

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _folderPath = "";
    [ObservableProperty] private int _audioFileCount;
    [ObservableProperty] private bool _needsReorganize;
    [ObservableProperty] private string? _expectedPath;

    // Cover image
    [ObservableProperty] private Bitmap? _coverImage;
    [ObservableProperty] private string? _coverImageSource;

    // Metadata source display
    [ObservableProperty] private string _mp3TagsSummary = "";
    [ObservableProperty] private string _bookinfoContent = "";
    [ObservableProperty] private string _nfoContent = "";
    [ObservableProperty] private bool _hasMp3Tags;
    [ObservableProperty] private bool _hasBookinfo;
    [ObservableProperty] private bool _hasNfo;

    // Folder files
    public ObservableCollection<FolderFileInfo> FolderFiles { get; } = [];

    public BookDetailViewModel(BookNode bookNode, IMetadataJsonProcessor metadataProcessor,
        IFileOrganizer fileOrganizer, IPathGenerator pathGenerator,
        string libraryPath, Func<string?, Task> reloadCallback, ILogger logger)
    {
        _bookNode = bookNode;
        _metadataProcessor = metadataProcessor;
        _fileOrganizer = fileOrganizer;
        _pathGenerator = pathGenerator;
        _libraryPath = libraryPath;
        _reloadCallback = reloadCallback;
        _logger = logger;

        FolderPath = bookNode.Path;
        AudioFileCount = bookNode.AudioFileCount;
        NeedsReorganize = bookNode.NeedsReorganize;
        ExpectedPath = bookNode.ExpectedPath;

        // Load current values
        LoadFromBookNode();

        // Load metadata sources lazily
        LoadMetadataSources();

        // Load cover image
        LoadCoverImage();

        // Load folder file list
        LoadFolderFiles();
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
        DiscNumber = _bookNode.DiscNumber?.ToString();
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

    private static readonly string[] CoverFileNames =
        ["cover", "folder", "front", "albumart", "album", "thumb", "thumbnail"];

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    private void LoadCoverImage()
    {
        var folderPath = _bookNode.Path;

        // 1. Look for image files in the folder
        var coverPath = FindFolderCoverImage(folderPath);
        if (coverPath != null)
        {
            try
            {
                CoverImage = new Bitmap(coverPath);
                CoverImageSource = System.IO.Path.GetFileName(coverPath);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load cover image: {Path}", coverPath);
            }
        }

        // 2. Fall back to embedded MP3 cover art
        TryExtractEmbeddedCover(folderPath);
    }

    private static string? FindFolderCoverImage(string folderPath)
    {
        try
        {
            var files = Directory.GetFiles(folderPath);
            // First pass: match known cover filenames
            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ImageExtensions.Contains(ext) && CoverFileNames.Contains(name))
                    return file;
            }
            // Second pass: any image file
            foreach (var file in files)
            {
                var ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                if (ImageExtensions.Contains(ext))
                    return file;
            }
        }
        catch
        {
            // Folder may not be accessible
        }
        return null;
    }

    private void TryExtractEmbeddedCover(string folderPath)
    {
        try
        {
            // Find first MP3 file
            var mp3File = Directory.EnumerateFiles(folderPath, "*.mp3").FirstOrDefault();
            if (mp3File == null)
                return;

            using var tagFile = TagLib.File.Create(mp3File);
            if (tagFile.Tag.Pictures.Length == 0)
                return;

            var picture = tagFile.Tag.Pictures[0];
            using var stream = new MemoryStream(picture.Data.Data);
            CoverImage = new Bitmap(stream);
            CoverImageSource = "embedded in MP3";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No embedded cover art found in {Path}", folderPath);
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
    partial void OnDiscNumberChanged(string? value) => IsDirty = true;
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

            int? discNumberInt = null;
            if (!string.IsNullOrWhiteSpace(DiscNumber) && int.TryParse(DiscNumber, out var parsedDisc))
                discNumberInt = parsedDisc;

            var metadata = new MetadataOverride
            {
                Author = NullIfEmpty(Author),
                Title = NullIfEmpty(Title),
                Series = NullIfEmpty(Series),
                SeriesNumber = NullIfEmpty(SeriesNumber),
                Narrator = NullIfEmpty(Narrator),
                Year = yearInt,
                DiscNumber = discNumberInt,
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
            _bookNode.DiscNumber = discNumberInt;
            _bookNode.Publisher = Publisher;
            _bookNode.Description = Description;
            _bookNode.Language = Language;

            // Recalculate expected path and reorganize status
            var bookMeta = new BookMetadata
            {
                Title = Title,
                Author = Author,
                Series = NullIfEmpty(Series),
                SeriesNumber = NullIfEmpty(SeriesNumber),
                Year = yearInt,
                DiscNumber = discNumberInt,
                Confidence = _bookNode.Confidence,
                Source = "GUI"
            };
            var newExpectedPath = _pathGenerator.GenerateTargetPath(bookMeta, _libraryPath);
            ExpectedPath = newExpectedPath;
            _bookNode.ExpectedPath = newExpectedPath;

            var needsMove = !string.Equals(
                _bookNode.Path.TrimEnd(Path.DirectorySeparatorChar),
                newExpectedPath.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            NeedsReorganize = needsMove;
            _bookNode.NeedsReorganize = needsMove;

            // Regenerate metadata.nfo to stay in sync
            var nfoFormatter = new NfoFormatter();
            var nfoMeta = new BookMetadata
            {
                Title = Title,
                Author = Author,
                Series = NullIfEmpty(Series),
                SeriesNumber = NullIfEmpty(SeriesNumber),
                Narrator = NullIfEmpty(Narrator),
                Year = yearInt,
                DiscNumber = discNumberInt,
                Genre = NullIfEmpty(Genre),
                Description = NullIfEmpty(Description),
                Language = NullIfEmpty(Language),
                Confidence = _bookNode.Confidence,
                Source = "manual"
            };
            var nfoContent = await nfoFormatter.FormatAsync(nfoMeta, ct);
            await File.WriteAllTextAsync(Path.Combine(_bookNode.Path, "metadata.nfo"), nfoContent, ct);
            NfoContent = nfoContent;
            HasNfo = true;

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

    [RelayCommand]
    private async Task ReorganizeBookAsync(CancellationToken ct)
    {
        if (!NeedsReorganize || string.IsNullOrEmpty(ExpectedPath) || _bookNode.SourceFolder == null)
            return;

        try
        {
            SaveStatus = "Moving...";

            var metadata = new BookMetadata
            {
                Title = _bookNode.Title,
                Author = _bookNode.Author,
                Series = _bookNode.Series,
                SeriesNumber = _bookNode.SeriesNumber,
                Narrator = _bookNode.Narrator,
                Year = _bookNode.Year,
                DiscNumber = _bookNode.DiscNumber,
                Genre = _bookNode.Genre,
                Description = _bookNode.Description,
                Language = _bookNode.Language,
                Confidence = _bookNode.Confidence,
                Source = "GUI"
            };

            var plan = new OrganizationPlan
            {
                SourceFolder = _bookNode.SourceFolder,
                Metadata = metadata,
                TargetPath = ExpectedPath,
                OperationType = FileOperationType.Move
            };

            var result = await _fileOrganizer.OrganizeFromPlansAsync([plan], false, null, ct);
            if (result.Success)
            {
                await _fileOrganizer.CleanupEmptyDirectoriesAsync(_libraryPath);
                SaveStatus = "Moved. Reloading...";
                await _reloadCallback(ExpectedPath);
            }
            else
            {
                SaveStatus = $"Move failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorganize book {Path}", _bookNode.Path);
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteBookAsync(CancellationToken ct)
    {
        try
        {
            var trashDir = Path.Combine(_libraryPath, ".trash");
            Directory.CreateDirectory(trashDir);

            var folderName = new DirectoryInfo(_bookNode.Path).Name;
            var trashTarget = Path.Combine(trashDir, folderName);

            // Ensure unique target path
            if (Directory.Exists(trashTarget))
            {
                var suffix = DateTime.Now.ToString("yyyyMMddHHmmss");
                trashTarget = Path.Combine(trashDir, $"{folderName}_{suffix}");
            }

            SaveStatus = "Moving to trash...";
            Directory.Move(_bookNode.Path, trashTarget);

            // Cleanup empty parent directories
            await _fileOrganizer.CleanupEmptyDirectoriesAsync(_libraryPath);

            SaveStatus = "Deleted. Reloading...";
            await _reloadCallback(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete book {Path}", _bookNode.Path);
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenInFinder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _bookNode.Path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder {Path}", _bookNode.Path);
        }
    }

    private void LoadFolderFiles()
    {
        try
        {
            var entries = new DirectoryInfo(_bookNode.Path)
                .EnumerateFiles()
                .OrderBy(f => f.Name)
                .Select(f => new FolderFileInfo
                {
                    Name = f.Name,
                    Size = FormatFileSize(f.Length),
                    Extension = f.Extension.ToLowerInvariant()
                });

            foreach (var entry in entries)
                FolderFiles.Add(entry);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate files in {Path}", _bookNode.Path);
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

public record FolderFileInfo
{
    public required string Name { get; init; }
    public required string Size { get; init; }
    public required string Extension { get; init; }
}
