using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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

    // Filter: show only misplaced books
    [ObservableProperty]
    private bool _filterMisplacedOnly;

    [ObservableProperty]
    private int _misplacedCount;

    // Reorganize properties
    [ObservableProperty]
    private string _destinationPath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsDestination))]
    private int _reorganizeModeIndex; // 0=In-place, 1=Copy to, 2=Move to

    [ObservableProperty]
    private bool _isReorganizing;

    public bool NeedsDestination => ReorganizeModeIndex > 0;

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
        ReorganizeModeIndex = settings.ReorganizeModeIndex;
    }

    partial void OnLibraryPathChanged(string value)
    {
        _settings.LibraryPath = value;
    }

    partial void OnDestinationPathChanged(string value)
    {
        _settings.DestinationPath = value;
    }

    partial void OnReorganizeModeIndexChanged(int value)
    {
        _settings.ReorganizeModeIndex = value;
    }

    [RelayCommand]
    private async Task BrowseLibraryAsync()
    {
        var path = await BrowseFolderAsync("Select library folder");
        if (path != null)
        {
            LibraryPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        var path = await BrowseFolderAsync("Select destination folder");
        if (path != null)
        {
            DestinationPath = path;
        }
    }

    private static async Task<string?> BrowseFolderAsync(string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        var mainWindow = desktop.MainWindow;
        if (mainWindow == null)
            return null;

        var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    partial void OnSelectedItemChanged(object? value)
    {
        // Auto-save previous detail if dirty
        switch (SelectedDetail)
        {
            case BookDetailViewModel { IsDirty: true } vm:
                vm.SaveCommand.Execute(null);
                break;
            case AuthorDetailViewModel { IsDirty: true } vm:
                vm.SaveCommand.Execute(null);
                break;
            case SeriesDetailViewModel { IsDirty: true } vm:
                vm.SaveCommand.Execute(null);
                break;
            case VolumeDetailViewModel { IsDirty: true } vm:
                vm.SaveCommand.Execute(null);
                break;
        }

        SelectedDetail = value switch
        {
            BookNode book => new BookDetailViewModel(book, _metadataProcessor, _fileOrganizer, _pathGenerator, LibraryPath, ReloadAndReselectAsync, _logger),
            AuthorNode author => new AuthorDetailViewModel(author, LibraryPath, _metadataProcessor, _fileOrganizer, _pathGenerator, ReloadAndReselectAsync, _logger),
            SeriesNode series => new SeriesDetailViewModel(series, LibraryPath, _metadataProcessor, _logger),
            VolumeNode volume => new VolumeDetailViewModel(volume, _metadataProcessor, _logger),
            _ => null
        };
    }

    /// <summary>
    /// Reloads the library and re-selects the node.
    /// If <paramref name="reselectPath"/> is provided, selects the book at that path.
    /// Otherwise, remembers the current selection and restores it after reload.
    /// </summary>
    private async Task ReloadAndReselectAsync(string? reselectPath)
    {
        // Remember current selection identity before reload clears the tree
        string? reselectAuthor = null;
        string? reselectSeries = null;

        if (reselectPath == null)
        {
            switch (SelectedItem)
            {
                case BookNode book:
                    reselectPath = book.ExpectedPath ?? book.Path;
                    break;
                case AuthorNode author:
                    reselectAuthor = author.Name;
                    break;
                case SeriesNode series:
                    reselectSeries = series.Name;
                    break;
            }
        }

        await ScanLibraryInternalAsync(cacheOnly: true, CancellationToken.None);

        if (reselectPath != null)
        {
            var book = AllBooks.FirstOrDefault(b =>
                string.Equals(b.Path, reselectPath, StringComparison.OrdinalIgnoreCase));
            if (book != null)
                SelectedItem = book;
        }
        else if (reselectAuthor != null)
        {
            var author = Authors.FirstOrDefault(a =>
                string.Equals(a.Name, reselectAuthor, StringComparison.OrdinalIgnoreCase));
            if (author != null)
                SelectedItem = author;
        }
        else if (reselectSeries != null)
        {
            var series = Authors.SelectMany(a => a.Children.OfType<SeriesNode>())
                .FirstOrDefault(s => string.Equals(s.Name, reselectSeries, StringComparison.OrdinalIgnoreCase));
            if (series != null)
                SelectedItem = series;
        }
    }

    /// <summary>
    /// Load library using full pipeline (folder structure + bookinfo.json + cached MP3 tags).
    /// Does NOT read actual MP3 files — use Scan Metadata for that.
    /// </summary>
    [RelayCommand]
    private async Task LoadLibraryAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = $"Path not found: {LibraryPath}";
            return;
        }

        await ScanLibraryInternalAsync(cacheOnly: true, ct);
    }

    /// <summary>
    /// Full scan: reads actual MP3 files and creates/updates mp3tags.json cache.
    /// </summary>
    [RelayCommand]
    private async Task ScanMetadataAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LibraryPath) || !Directory.Exists(LibraryPath))
        {
            StatusText = $"Path not found: {LibraryPath}";
            return;
        }

        await ScanLibraryInternalAsync(cacheOnly: false, ct);
    }

    /// <summary>
    /// Shared scan logic. cacheOnly=true skips MP3 reading (fast), false reads actual files.
    /// </summary>
    private async Task ScanLibraryInternalAsync(bool cacheOnly, CancellationToken ct)
    {
        IsLoading = true;
        StatusText = cacheOnly ? "Loading library..." : "Scanning folders...";
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
                    var metadata = cacheOnly
                        ? await _metadataExtractor.ExtractMetadataCachedOnlyAsync(folder, LibraryPath, ct)
                        : await _metadataExtractor.ExtractMetadataAsync(folder, LibraryPath, ct);

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

            _allAuthors = authorNodes;
            RebuildFlatBookList();
            var mode = cacheOnly ? "Loaded" : "Scanned";
            var misplacedInfo = MisplacedCount > 0 ? $" ({MisplacedCount} misplaced)" : "";
            StatusText = $"{mode} {AllBooks.Count} book(s) from {Authors.Count} author(s).{misplacedInfo}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
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
    /// Mode 0: In-place (move within library path)
    /// Mode 1: Copy to destination
    /// Mode 2: Move to destination
    /// </summary>
    [RelayCommand]
    private async Task ReorganizeAsync(CancellationToken ct)
    {
        var isInPlace = ReorganizeModeIndex == 0;
        var destPath = isInPlace ? LibraryPath : DestinationPath;
        var opType = ReorganizeModeIndex switch
        {
            0 => FileOperationType.Move, // in-place = move within same root
            1 => FileOperationType.Copy,
            2 => FileOperationType.Move,
            _ => FileOperationType.Copy
        };

        if (string.IsNullOrWhiteSpace(destPath) || !Directory.Exists(destPath))
        {
            StatusText = isInPlace ? "Load a library first." : "Set a valid destination path.";
            return;
        }

        var booksWithFolders = AllBooks.Where(b => b.SourceFolder != null).ToList();
        if (booksWithFolders.Count == 0)
        {
            StatusText = "No scanned books to reorganize. Run 'Scan Metadata' first.";
            return;
        }

        IsReorganizing = true;
        var modeLabel = isInPlace ? "in-place" : $"to {destPath}";
        StatusText = $"Building plans for {booksWithFolders.Count} book(s) ({modeLabel})...";

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
                    DiscNumber = book.DiscNumber,
                    Genre = book.Genre,
                    Description = book.Description,
                    Language = book.Language,
                    Confidence = book.Confidence,
                    Source = "GUI"
                };

                var targetPath = _pathGenerator.GenerateTargetPath(metadata, destPath);

                plans.Add(new OrganizationPlan
                {
                    SourceFolder = book.SourceFolder!,
                    Metadata = metadata,
                    TargetPath = targetPath,
                    OperationType = opType
                });
            }

            var progress = new Progress<OrganizationProgress>(p =>
            {
                StatusText = $"Organizing... {p.AudiobooksCompleted}/{p.TotalAudiobooks} ({p.PercentComplete:F0}%)";
            });

            var result = await _fileOrganizer.OrganizeFromPlansAsync(plans, true, progress, ct);

            if (result.Success)
            {
                StatusText = $"Organized {result.SuccessfulAudiobooks}/{result.TotalAudiobooks} book(s) {modeLabel}. Reloading...";
                if (isInPlace)
                    await _fileOrganizer.CleanupEmptyDirectoriesAsync(LibraryPath);
                await ReloadAndReselectAsync(null);
                StatusText = $"Organized {result.SuccessfulAudiobooks}/{result.TotalAudiobooks} book(s) {modeLabel}";
            }
            else
            {
                StatusText = $"Completed with errors: {result.SuccessfulAudiobooks}/{result.TotalAudiobooks} succeeded. {result.ErrorMessage}";
            }
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

    [RelayCommand]
    private async Task ExportNfoAsync(CancellationToken ct)
    {
        if (AllBooks.Count == 0)
        {
            StatusText = "No books loaded. Load a library first.";
            return;
        }

        var nfoFormatter = new NfoFormatter();
        var exported = 0;

        StatusText = $"Exporting NFO for {AllBooks.Count} book(s)...";

        foreach (var book in AllBooks)
        {
            ct.ThrowIfCancellationRequested();

            var metadata = new BookMetadata
            {
                Title = book.Title,
                Author = book.Author,
                Series = book.Series,
                SeriesNumber = book.SeriesNumber,
                Narrator = book.Narrator,
                Year = book.Year,
                DiscNumber = book.DiscNumber,
                Genre = book.Genre,
                Description = book.Description,
                Language = book.Language,
                Confidence = book.Confidence,
                Source = "GUI"
            };

            try
            {
                var nfoContent = await nfoFormatter.FormatAsync(metadata, ct);
                var nfoPath = System.IO.Path.Combine(book.Path, "metadata.nfo");
                await File.WriteAllTextAsync(nfoPath, nfoContent, ct);
                exported++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write NFO for {Path}", book.Path);
            }
        }

        StatusText = $"Exported {exported} metadata.nfo file(s).";
    }

    private BookNode CreateBookNodeFromMetadata(BookMetadata meta, AudiobookFolder folder)
    {
        var bookinfoPath = System.IO.Path.Combine(folder.Path, "bookinfo.json");
        var expectedPath = _pathGenerator.GenerateTargetPath(meta, LibraryPath);
        var needsReorganize = !string.Equals(
            folder.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar),
            expectedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

        var node = new BookNode
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
            DiscNumber = meta.DiscNumber,
            Publisher = null,
            Description = meta.Description,
            Language = meta.Language,
            Confidence = meta.Confidence,
            AudioFileCount = folder.FileCount,
            HasBookinfo = File.Exists(bookinfoPath),
            IsManual = meta.Source.Equals("Manual", StringComparison.OrdinalIgnoreCase),
            HasMp3TagsCache = File.Exists(System.IO.Path.Combine(folder.Path, "mp3tags.json")),
            HasNfo = File.Exists(System.IO.Path.Combine(folder.Path, "metadata.nfo")),
            IsMultiDisc = folder.IsMultiDisc,
            DiscCount = folder.DiscSubfolders.Count,
            SourceFolder = folder,
            NeedsReorganize = needsReorganize,
            ExpectedPath = expectedPath
        };

        // Populate volume children for multi-disc books
        if (folder.IsMultiDisc)
        {
            foreach (var discName in folder.DiscSubfolders)
            {
                var discPath = System.IO.Path.Combine(folder.Path, discName);
                var fileCount = Directory.Exists(discPath)
                    ? Directory.EnumerateFiles(discPath).Count()
                    : 0;
                node.Volumes.Add(new VolumeNode
                {
                    Name = discName,
                    Path = discPath,
                    FileCount = fileCount
                });
            }
        }

        return node;
    }

    // Unfiltered backing store
    private ObservableCollection<AuthorNode> _allAuthors = [];
    private ObservableCollection<BookNode> _allBooksUnfiltered = [];

    partial void OnFilterMisplacedOnlyChanged(bool value) => ApplyFilter();

    private void RebuildFlatBookList()
    {
        _allBooksUnfiltered = CollectAllBooks(_allAuthors);
        MisplacedCount = _allBooksUnfiltered.Count(b => b.NeedsReorganize);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (!FilterMisplacedOnly)
        {
            Authors = _allAuthors;
            AllBooks = _allBooksUnfiltered;
            return;
        }

        // Filter: only books with NeedsReorganize
        var filteredAuthors = new ObservableCollection<AuthorNode>();
        var filteredBooks = new ObservableCollection<BookNode>();

        foreach (var author in _allAuthors)
        {
            var filteredAuthor = new AuthorNode { Name = author.Name, Path = author.Path };

            foreach (var child in author.Children)
            {
                if (child is BookNode book && book.NeedsReorganize)
                {
                    filteredAuthor.Children.Add(book);
                    filteredBooks.Add(book);
                }
                else if (child is SeriesNode series)
                {
                    var misplaced = series.Books.Where(b => b.NeedsReorganize).ToList();
                    if (misplaced.Count > 0)
                    {
                        var filteredSeries = new SeriesNode { Name = series.Name, Path = series.Path };
                        foreach (var b in misplaced)
                        {
                            filteredSeries.Books.Add(b);
                            filteredBooks.Add(b);
                        }
                        filteredAuthor.Children.Add(filteredSeries);
                    }
                }
            }

            if (filteredAuthor.Children.Count > 0)
                filteredAuthors.Add(filteredAuthor);
        }

        Authors = filteredAuthors;
        AllBooks = filteredBooks;
    }

    private static ObservableCollection<BookNode> CollectAllBooks(ObservableCollection<AuthorNode> authors)
    {
        var books = new ObservableCollection<BookNode>();
        foreach (var author in authors)
        {
            foreach (var child in author.Children)
            {
                if (child is BookNode book)
                    books.Add(book);
                else if (child is SeriesNode series)
                {
                    foreach (var b in series.Books)
                        books.Add(b);
                }
            }
        }
        return books;
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
    [ObservableProperty] private int? _discNumber;
    [ObservableProperty] private string? _publisher;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _language;
    [ObservableProperty] private double _confidence;
    public int AudioFileCount { get; init; }
    public bool HasBookinfo { get; init; }
    public bool IsManual { get; init; }
    public bool HasMp3TagsCache { get; init; }
    public bool HasNfo { get; init; }

    [ObservableProperty] private bool _isMultiDisc;
    [ObservableProperty] private int _discCount;
    [ObservableProperty] private bool _needsReorganize;
    [ObservableProperty] private string? _expectedPath;

    /// <summary>The scanned AudiobookFolder, if loaded via metadata scan.</summary>
    public AudiobookFolder? SourceFolder { get; set; }

    /// <summary>Volume (disc) children for multi-disc books.</summary>
    public ObservableCollection<VolumeNode> Volumes { get; } = [];
}

public class VolumeNode
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public int FileCount { get; init; }
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
