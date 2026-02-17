using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Desktop.Services;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class AuthorDetailViewModel : ObservableObject
{
    private readonly AuthorNode _authorNode;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly IFileOrganizer _fileOrganizer;
    private readonly IPathGenerator _pathGenerator;
    private readonly PublishQueueService _publishQueue;
    private readonly AppSettings _settings;
    private readonly Func<string?, Task> _reloadCallback;
    private readonly ILogger _logger;
    private readonly string _libraryPath;

    [ObservableProperty] private string _authorName;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _folderPath;
    [ObservableProperty] private int _bookCount;
    [ObservableProperty] private int _seriesCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBooksNeedingReorganize))]
    private int _booksNeedingReorganize;

    public bool HasBooksNeedingReorganize => BooksNeedingReorganize > 0;

    [ObservableProperty] private int _unpublishedCount;
    [ObservableProperty] private string _publishStatus = "";
    [ObservableProperty] private bool _canPublish;

    private readonly string _originalName;

    public AuthorDetailViewModel(AuthorNode authorNode, string libraryPath,
        IMetadataJsonProcessor metadataProcessor, IFileOrganizer fileOrganizer,
        IPathGenerator pathGenerator, PublishQueueService publishQueue,
        AppSettings settings, Func<string?, Task> reloadCallback, ILogger logger)
    {
        _authorNode = authorNode;
        _libraryPath = libraryPath;
        _metadataProcessor = metadataProcessor;
        _fileOrganizer = fileOrganizer;
        _pathGenerator = pathGenerator;
        _publishQueue = publishQueue;
        _settings = settings;
        _reloadCallback = reloadCallback;
        _logger = logger;

        _originalName = authorNode.Name;
        _authorName = authorNode.Name;
        _folderPath = authorNode.Path;

        _bookCount = CountBooks(authorNode);
        _seriesCount = authorNode.Children.OfType<SeriesNode>().Count();
        _booksNeedingReorganize = GetAllBooks(authorNode).Count(b => b.NeedsReorganize);
        _unpublishedCount = GetAllBooks(authorNode).Count(b => !b.IsPublished);
        _canPublish = _unpublishedCount > 0 && !string.IsNullOrWhiteSpace(settings.AbsLibraryFolder);
    }

    partial void OnAuthorNameChanged(string value)
    {
        IsDirty = !string.Equals(value, _originalName, StringComparison.Ordinal);
    }

    [RelayCommand]
    private async Task SaveAsync(CancellationToken ct)
    {
        if (!IsDirty || string.IsNullOrWhiteSpace(AuthorName))
            return;

        try
        {
            SaveStatus = "Saving...";

            var count = await _metadataProcessor.BatchUpdateAuthorAsync(
                _libraryPath, _originalName, AuthorName.Trim(), ct);

            // Also update all BookNode objects in the tree
            UpdateTreeNodes(_authorNode, AuthorName.Trim());

            IsDirty = false;
            SaveStatus = $"Updated {count} bookinfo.json file(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save author rename");
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Revert()
    {
        AuthorName = _originalName;
        IsDirty = false;
        SaveStatus = "";
    }

    [RelayCommand]
    private async Task ReorganizeAuthorAsync(CancellationToken ct)
    {
        var booksToMove = GetAllBooks(_authorNode)
            .Where(b => b.NeedsReorganize && b.SourceFolder != null && !string.IsNullOrEmpty(b.ExpectedPath))
            .ToList();

        if (booksToMove.Count == 0)
            return;

        try
        {
            SaveStatus = $"Moving {booksToMove.Count} book(s)...";

            var plans = new List<OrganizationPlan>();
            foreach (var book in booksToMove)
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

                plans.Add(new OrganizationPlan
                {
                    SourceFolder = book.SourceFolder!,
                    Metadata = metadata,
                    TargetPath = book.ExpectedPath!,
                    OperationType = FileOperationType.Move
                });
            }

            var result = await _fileOrganizer.OrganizeFromPlansAsync(plans, false, null, ct);
            if (result.Success)
            {
                await _fileOrganizer.CleanupEmptyDirectoriesAsync(_libraryPath);
                SaveStatus = $"Moved {result.SuccessfulAudiobooks} book(s). Reloading...";
                await _reloadCallback(null);
            }
            else
            {
                SaveStatus = $"Move failed: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorganize author {Author}", _authorNode.Name);
            SaveStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PublishAll()
    {
        if (string.IsNullOrWhiteSpace(_settings.AbsLibraryFolder))
        {
            PublishStatus = "ABS library folder not configured.";
            return;
        }

        var unpublished = GetAllBooks(_authorNode)
            .Where(b => !b.IsPublished)
            .ToList();

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

    private static int CountBooks(AuthorNode author)
    {
        return GetAllBooks(author).Count();
    }

    private static IEnumerable<BookNode> GetAllBooks(AuthorNode author)
    {
        foreach (var child in author.Children)
        {
            if (child is BookNode book)
                yield return book;
            else if (child is SeriesNode series)
            {
                foreach (var book2 in series.Books)
                    yield return book2;
            }
        }
    }

    private static void UpdateTreeNodes(AuthorNode author, string newAuthor)
    {
        foreach (var book in GetAllBooks(author))
            book.Author = newAuthor;
    }
}
