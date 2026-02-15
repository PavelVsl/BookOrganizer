using System;
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

public partial class LibraryViewModel : ObservableObject
{
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger<LibraryViewModel> _logger;

    [ObservableProperty]
    private string _libraryPath = "";

    [ObservableProperty]
    private ObservableCollection<AuthorNode> _authors = [];

    [ObservableProperty]
    private object? _selectedItem;

    [ObservableProperty]
    private BookDetailViewModel? _selectedBookDetail;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Select a library folder to begin.";

    public LibraryViewModel(
        IMetadataJsonProcessor metadataProcessor,
        ILogger<LibraryViewModel> logger)
    {
        _metadataProcessor = metadataProcessor;
        _logger = logger;

        // Try default from env var
        var envPath = Environment.GetEnvironmentVariable("BOOKORGANIZER_LIBRARY");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            LibraryPath = envPath;
        }
    }

    partial void OnSelectedItemChanged(object? value)
    {
        if (value is BookNode book)
        {
            SelectedBookDetail = new BookDetailViewModel(book, _metadataProcessor, _logger);
        }
        else
        {
            SelectedBookDetail = null;
        }
    }

    [RelayCommand]
    private async Task LoadLibraryAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = "Invalid library path.";
            return;
        }

        IsLoading = true;
        StatusText = "Loading library...";
        Authors.Clear();

        try
        {
            await Task.Run(() => ScanLibraryStructure(ct), ct);
            StatusText = $"Loaded {Authors.Count} author(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Loading cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load library");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ScanLibraryStructure(CancellationToken ct)
    {
        // Library structure: Author/[Series/]Book/
        // Each folder may have bookinfo.json
        var libraryDir = new DirectoryInfo(LibraryPath);
        var authorNodes = new ObservableCollection<AuthorNode>();

        foreach (var authorDir in libraryDir.EnumerateDirectories()
            .Where(d => !d.Name.StartsWith('.'))
            .OrderBy(d => d.Name))
        {
            ct.ThrowIfCancellationRequested();

            var authorNode = new AuthorNode { Name = authorDir.Name, Path = authorDir.FullName };

            foreach (var subDir in authorDir.EnumerateDirectories()
                .Where(d => !d.Name.StartsWith('.'))
                .OrderBy(d => d.Name))
            {
                // Check if this is a book folder (contains audio files) or a series folder
                var hasAudio = HasAudioFiles(subDir);

                if (hasAudio)
                {
                    // Direct book under author (no series)
                    var bookNode = CreateBookNode(subDir, authorDir.Name, null);
                    authorNode.Children.Add(bookNode);
                }
                else
                {
                    // Series folder
                    var seriesNode = new SeriesNode { Name = subDir.Name, Path = subDir.FullName };

                    foreach (var bookDir in subDir.EnumerateDirectories()
                        .Where(d => !d.Name.StartsWith('.'))
                        .OrderBy(d => d.Name))
                    {
                        ct.ThrowIfCancellationRequested();

                        if (HasAudioFiles(bookDir))
                        {
                            var bookNode = CreateBookNode(bookDir, authorDir.Name, subDir.Name);
                            seriesNode.Books.Add(bookNode);
                        }
                    }

                    if (seriesNode.Books.Count > 0)
                    {
                        authorNode.Children.Add(seriesNode);
                    }
                }
            }

            if (authorNode.Children.Count > 0)
            {
                authorNodes.Add(authorNode);
            }
        }

        Authors = authorNodes;
    }

    private BookNode CreateBookNode(DirectoryInfo dir, string authorName, string? seriesName)
    {
        var bookinfoPath = System.IO.Path.Combine(dir.FullName, "bookinfo.json");
        MetadataOverride? metadata = null;

        if (File.Exists(bookinfoPath))
        {
            try
            {
                var json = File.ReadAllText(bookinfoPath);
                metadata = JsonSerializer.Deserialize<MetadataOverride>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load bookinfo.json from {Path}", dir.FullName);
            }
        }

        var audioFiles = dir.EnumerateFiles("*.mp3", SearchOption.TopDirectoryOnly).Count();

        return new BookNode
        {
            FolderName = dir.Name,
            Path = dir.FullName,
            Author = metadata?.Author ?? authorName,
            Title = metadata?.Title ?? dir.Name,
            Series = metadata?.Series ?? seriesName,
            SeriesNumber = metadata?.SeriesNumber,
            Narrator = metadata?.Narrator,
            Year = metadata?.Year,
            Genre = metadata?.Genre,
            Publisher = metadata?.Publisher,
            Description = metadata?.Description,
            Language = metadata?.Language,
            AudioFileCount = audioFiles,
            HasBookinfo = File.Exists(bookinfoPath),
            IsManual = metadata?.Source?.Equals(MetadataOverride.ManualSource, StringComparison.OrdinalIgnoreCase) == true
        };
    }

    private static bool HasAudioFiles(DirectoryInfo dir)
    {
        return dir.EnumerateFiles("*.mp3", SearchOption.TopDirectoryOnly).Any()
            || dir.EnumerateFiles("*.m4b", SearchOption.TopDirectoryOnly).Any()
            || dir.EnumerateFiles("*.m4a", SearchOption.TopDirectoryOnly).Any();
    }
}

public class AuthorNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ObservableCollection<object> Children { get; } = [];
}

public class SeriesNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ObservableCollection<BookNode> Books { get; } = [];
}

public partial class BookNode : ObservableObject
{
    public required string FolderName { get; init; }
    public required string Path { get; init; }

    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string? _series;
    [ObservableProperty] private string? _seriesNumber;
    [ObservableProperty] private string? _narrator;
    [ObservableProperty] private int? _year;
    [ObservableProperty] private string? _genre;
    [ObservableProperty] private string? _publisher;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _language;
    public int AudioFileCount { get; init; }
    public bool HasBookinfo { get; init; }
    public bool IsManual { get; init; }
}
