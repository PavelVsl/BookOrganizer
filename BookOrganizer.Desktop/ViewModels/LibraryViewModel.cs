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
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly ITextNormalizer _textNormalizer;
    private readonly IFileOrganizer _fileOrganizer;
    private readonly IPathGenerator _pathGenerator;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _libraryPath = "";

    [ObservableProperty]
    private ObservableCollection<AuthorNode> _authors = [];

    [ObservableProperty]
    private object? _selectedItem;

    [ObservableProperty]
    private object? _selectedDetail;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Select a library folder to begin.";

    // Flat list of all books for DataGrid binding
    [ObservableProperty]
    private ObservableCollection<BookNode> _allBooks = [];

    // Reorganize properties
    [ObservableProperty]
    private string _destinationPath = "";

    [ObservableProperty]
    private FileOperationType _operationType = FileOperationType.Copy;

    [ObservableProperty]
    private int _operationTypeIndex;

    [ObservableProperty]
    private bool _isReorganizing;

    // Synonym detection
    [ObservableProperty]
    private ObservableCollection<NameSynonymGroup> _synonymGroups = [];

    [ObservableProperty]
    private bool _showSynonymPanel;

    public LibraryViewModel(
        IMetadataJsonProcessor metadataProcessor,
        IDirectoryScanner directoryScanner,
        IMetadataExtractor metadataExtractor,
        ITextNormalizer textNormalizer,
        IFileOrganizer fileOrganizer,
        IPathGenerator pathGenerator,
        ILogger<LibraryViewModel> logger,
        AppSettings settings)
    {
        _metadataProcessor = metadataProcessor;
        _directoryScanner = directoryScanner;
        _metadataExtractor = metadataExtractor;
        _textNormalizer = textNormalizer;
        _fileOrganizer = fileOrganizer;
        _pathGenerator = pathGenerator;
        _logger = logger;
        _settings = settings;

        // Restore from settings, fall back to env var
        var path = settings.LibraryPath
            ?? Environment.GetEnvironmentVariable("BOOKORGANIZER_LIBRARY");
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        {
            LibraryPath = path;
        }

        DestinationPath = settings.DestinationPath ?? "";
        OperationType = settings.LastOperationType;
        OperationTypeIndex = (int)OperationType;
    }

    partial void OnLibraryPathChanged(string value)
    {
        _settings.LibraryPath = value;
    }

    partial void OnDestinationPathChanged(string value)
    {
        _settings.DestinationPath = value;
    }

    partial void OnOperationTypeChanged(FileOperationType value)
    {
        _settings.LastOperationType = value;
    }

    partial void OnOperationTypeIndexChanged(int value)
    {
        OperationType = (FileOperationType)value;
    }

    partial void OnSelectedItemChanged(object? value)
    {
        SelectedDetail = value switch
        {
            BookNode book => new BookDetailViewModel(book, _metadataProcessor, _logger),
            AuthorNode author => new AuthorDetailViewModel(author, LibraryPath, _metadataProcessor, _logger),
            SeriesNode series => new SeriesDetailViewModel(series, LibraryPath, _metadataProcessor, _logger),
            _ => null
        };
    }

    /// <summary>
    /// Fast load: reads folder structure + bookinfo.json only.
    /// </summary>
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
        AllBooks.Clear();

        try
        {
            await Task.Run(() => ScanLibraryStructure(ct), ct);
            RebuildFlatBookList();
            StatusText = $"Loaded {Authors.Count} author(s), {AllBooks.Count} book(s).";
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

    /// <summary>
    /// Full scan: uses IDirectoryScanner + IMetadataExtractor for consolidated metadata.
    /// </summary>
    [RelayCommand]
    private async Task ScanMetadataAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = "Invalid library path.";
            return;
        }

        IsLoading = true;
        StatusText = "Scanning folders...";
        Authors.Clear();
        AllBooks.Clear();

        try
        {
            // Step 1: Scan for audiobook folders
            var scanProgress = new Progress<ScanProgress>(p =>
            {
                StatusText = $"Scanning... {p.AudiobookFoldersFound} audiobook(s) found in {p.DirectoriesScanned} directories";
            });

            var folders = await _directoryScanner.ScanDirectoryAsync(LibraryPath, scanProgress, ct);
            StatusText = $"Found {folders.Count} audiobook(s). Extracting metadata...";

            // Step 2: Extract metadata for each folder
            var booksByAuthor = new Dictionary<string, List<(BookMetadata meta, AudiobookFolder folder)>>();
            var processed = 0;

            foreach (var folder in folders)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                try
                {
                    var metadata = await _metadataExtractor.ExtractMetadataAsync(folder, LibraryPath, ct);
                    var author = metadata.Author ?? "Unknown";

                    if (!booksByAuthor.TryGetValue(author, out var list))
                    {
                        list = [];
                        booksByAuthor[author] = list;
                    }
                    list.Add((metadata, folder));

                    if (processed % 5 == 0 || processed == folders.Count)
                    {
                        StatusText = $"Extracting metadata... {processed}/{folders.Count}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to extract metadata from {Path}", folder.Path);
                }
            }

            // Step 3: Build tree from extracted metadata
            var authorNodes = new ObservableCollection<AuthorNode>();

            foreach (var (author, books) in booksByAuthor.OrderBy(kv => kv.Key))
            {
                var authorNode = new AuthorNode { Name = author, Path = "" };

                // Group by series
                var withSeries = books.Where(b => !string.IsNullOrEmpty(b.meta.Series)).GroupBy(b => b.meta.Series!);
                var withoutSeries = books.Where(b => string.IsNullOrEmpty(b.meta.Series));

                foreach (var seriesGroup in withSeries.OrderBy(g => g.Key))
                {
                    var seriesNode = new SeriesNode { Name = seriesGroup.Key, Path = "" };
                    foreach (var (meta, folder) in seriesGroup.OrderBy(b => b.meta.SeriesNumber))
                    {
                        seriesNode.Books.Add(CreateBookNodeFromMetadata(meta, folder));
                    }
                    authorNode.Children.Add(seriesNode);
                }

                foreach (var (meta, folder) in withoutSeries.OrderBy(b => b.meta.Title))
                {
                    authorNode.Children.Add(CreateBookNodeFromMetadata(meta, folder));
                }

                authorNodes.Add(authorNode);
            }

            Authors = authorNodes;
            RebuildFlatBookList();
            StatusText = $"Scanned {AllBooks.Count} book(s) from {Authors.Count} author(s).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan metadata");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Detects similar author/narrator names and groups them.
    /// </summary>
    [RelayCommand]
    private void DetectSynonyms()
    {
        var groups = new ObservableCollection<NameSynonymGroup>();
        var threshold = 0.8;

        // Collect unique names with book counts
        var authorCounts = AllBooks
            .Where(b => !string.IsNullOrWhiteSpace(b.Author))
            .GroupBy(b => b.Author)
            .ToDictionary(g => g.Key, g => g.Count());

        var narratorCounts = AllBooks
            .Where(b => !string.IsNullOrWhiteSpace(b.Narrator))
            .GroupBy(b => b.Narrator!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Find similar authors using union-find
        FindSynonymGroups("Author", authorCounts, threshold, groups);
        FindSynonymGroups("Narrator", narratorCounts, threshold, groups);

        SynonymGroups = groups;
        ShowSynonymPanel = groups.Count > 0;
        StatusText = groups.Count > 0
            ? $"Found {groups.Count} synonym group(s)."
            : "No synonyms detected.";
    }

    private void FindSynonymGroups(
        string fieldName,
        Dictionary<string, int> nameCounts,
        double threshold,
        ObservableCollection<NameSynonymGroup> groups)
    {
        var names = nameCounts.Keys.ToList();
        var parent = new Dictionary<string, string>();
        foreach (var name in names) parent[name] = name;

        string Find(string x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        // Compare all pairs
        for (var i = 0; i < names.Count; i++)
        {
            for (var j = i + 1; j < names.Count; j++)
            {
                var sim = _textNormalizer.CalculateSimilarity(names[i], names[j]);
                if (sim >= threshold)
                {
                    Union(names[i], names[j]);
                }
            }
        }

        // Build groups from union-find
        var grouped = names.GroupBy(Find).Where(g => g.Count() > 1);

        foreach (var group in grouped)
        {
            var variants = group.Select(n => new NameVariant { Name = n, BookCount = nameCounts[n] }).ToList();
            var canonical = variants.OrderByDescending(v => v.BookCount).First().Name;

            groups.Add(new NameSynonymGroup
            {
                FieldName = fieldName,
                CanonicalName = canonical,
                Variants = new ObservableCollection<NameVariant>(variants)
            });
        }
    }

    /// <summary>
    /// Applies synonym canonical names to all books.
    /// </summary>
    [RelayCommand]
    private void ApplySynonyms()
    {
        var applied = 0;
        foreach (var group in SynonymGroups)
        {
            var variantNames = group.Variants.Select(v => v.Name).ToHashSet();

            foreach (var book in AllBooks)
            {
                if (group.FieldName == "Author" && variantNames.Contains(book.Author))
                {
                    book.Author = group.CanonicalName;
                    applied++;
                }
                else if (group.FieldName == "Narrator" && book.Narrator != null && variantNames.Contains(book.Narrator))
                {
                    book.Narrator = group.CanonicalName;
                    applied++;
                }
            }
        }

        ShowSynonymPanel = false;
        StatusText = $"Applied synonyms to {applied} book(s).";
    }

    /// <summary>
    /// Reorganizes books into structured Author/[Series/]Title/ layout.
    /// </summary>
    [RelayCommand]
    private async Task ReorganizeAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            StatusText = "Set a destination path first.";
            return;
        }

        var booksWithFolders = AllBooks.Where(b => b.SourceFolder != null).ToList();
        if (booksWithFolders.Count == 0)
        {
            StatusText = "No scanned books to reorganize. Run 'Scan Metadata' first.";
            return;
        }

        IsReorganizing = true;
        StatusText = $"Building plans for {booksWithFolders.Count} book(s)...";

        try
        {
            // Build organization plans
            var plans = new List<OrganizationPlan>();
            foreach (var book in booksWithFolders)
            {
                var metadata = new BookMetadata
                {
                    Title = book.Title,
                    Author = book.Author,
                    Series = book.Series,
                    SeriesNumber = book.SeriesNumber,
                    Narrator = book.Narrator,
                    Year = book.Year,
                    Genre = book.Genre,
                    Description = book.Description,
                    Language = book.Language,
                    Confidence = book.Confidence,
                    Source = "GUI"
                };

                var targetPath = _pathGenerator.GenerateTargetPath(metadata, DestinationPath);

                plans.Add(new OrganizationPlan
                {
                    SourceFolder = book.SourceFolder!,
                    Metadata = metadata,
                    TargetPath = targetPath,
                    OperationType = OperationType
                });
            }

            var progress = new Progress<OrganizationProgress>(p =>
            {
                StatusText = $"Organizing... {p.AudiobooksCompleted}/{p.TotalAudiobooks} ({p.PercentComplete:F0}%)";
            });

            var result = await _fileOrganizer.OrganizeFromPlansAsync(plans, true, progress, ct);

            StatusText = result.Success
                ? $"Organized {result.SuccessfulAudiobooks}/{result.TotalAudiobooks} book(s) to {DestinationPath}"
                : $"Completed with errors: {result.SuccessfulAudiobooks}/{result.TotalAudiobooks} succeeded. {result.ErrorMessage}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Reorganize cancelled.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorganize");
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsReorganizing = false;
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
            IsManual = metadata?.Source?.Equals(MetadataOverride.ManualSource, StringComparison.OrdinalIgnoreCase) == true,
            HasMp3TagsCache = File.Exists(System.IO.Path.Combine(dir.FullName, "mp3tags.json")),
            HasNfo = File.Exists(System.IO.Path.Combine(dir.FullName, "metadata.nfo"))
        };
    }

    private BookNode CreateBookNodeFromMetadata(BookMetadata meta, AudiobookFolder folder)
    {
        var bookinfoPath = System.IO.Path.Combine(folder.Path, "bookinfo.json");

        return new BookNode
        {
            FolderName = System.IO.Path.GetFileName(folder.Path),
            Path = folder.Path,
            Author = meta.Author ?? "Unknown",
            Title = meta.Title,
            Series = meta.Series,
            SeriesNumber = meta.SeriesNumber,
            Narrator = meta.Narrator,
            Year = meta.Year,
            Genre = meta.Genre,
            Publisher = null,
            Description = meta.Description,
            Language = meta.Language,
            Confidence = meta.Confidence,
            AudioFileCount = folder.FileCount,
            HasBookinfo = File.Exists(bookinfoPath),
            IsManual = meta.Source.Equals("Manual", StringComparison.OrdinalIgnoreCase),
            HasMp3TagsCache = File.Exists(System.IO.Path.Combine(folder.Path, "mp3tags.json")),
            HasNfo = File.Exists(System.IO.Path.Combine(folder.Path, "metadata.nfo")),
            SourceFolder = folder
        };
    }

    private void RebuildFlatBookList()
    {
        var books = new ObservableCollection<BookNode>();
        foreach (var author in Authors)
        {
            foreach (var child in author.Children)
            {
                if (child is BookNode book)
                {
                    books.Add(book);
                }
                else if (child is SeriesNode series)
                {
                    foreach (var b in series.Books)
                    {
                        books.Add(b);
                    }
                }
            }
        }
        AllBooks = books;
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
    [ObservableProperty] private double _confidence;
    public int AudioFileCount { get; init; }
    public bool HasBookinfo { get; init; }
    public bool IsManual { get; init; }
    public bool HasMp3TagsCache { get; init; }
    public bool HasNfo { get; init; }

    /// <summary>The scanned AudiobookFolder, if loaded via metadata scan.</summary>
    public AudiobookFolder? SourceFolder { get; set; }
}

// Synonym detection models
public partial class NameSynonymGroup : ObservableObject
{
    public required string FieldName { get; init; }
    [ObservableProperty] private string _canonicalName = "";
    public required ObservableCollection<NameVariant> Variants { get; init; }
}

public class NameVariant
{
    public required string Name { get; init; }
    public required int BookCount { get; init; }
}
