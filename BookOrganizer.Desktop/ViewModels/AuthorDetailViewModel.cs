using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Desktop.ViewModels;

public partial class AuthorDetailViewModel : ObservableObject
{
    private readonly AuthorNode _authorNode;
    private readonly IMetadataJsonProcessor _metadataProcessor;
    private readonly ILogger _logger;
    private readonly string _libraryPath;

    [ObservableProperty] private string _authorName;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _folderPath;
    [ObservableProperty] private int _bookCount;
    [ObservableProperty] private int _seriesCount;

    private readonly string _originalName;

    public AuthorDetailViewModel(AuthorNode authorNode, string libraryPath,
        IMetadataJsonProcessor metadataProcessor, ILogger logger)
    {
        _authorNode = authorNode;
        _libraryPath = libraryPath;
        _metadataProcessor = metadataProcessor;
        _logger = logger;

        _originalName = authorNode.Name;
        _authorName = authorNode.Name;
        _folderPath = authorNode.Path;

        _bookCount = CountBooks(authorNode);
        _seriesCount = authorNode.Children.OfType<SeriesNode>().Count();
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

    private static int CountBooks(AuthorNode author)
    {
        var count = 0;
        foreach (var child in author.Children)
        {
            if (child is BookNode) count++;
            else if (child is SeriesNode series) count += series.Books.Count;
        }
        return count;
    }

    private static void UpdateTreeNodes(AuthorNode author, string newAuthor)
    {
        foreach (var child in author.Children)
        {
            if (child is BookNode book)
                book.Author = newAuthor;
            else if (child is SeriesNode series)
            {
                foreach (var book2 in series.Books)
                    book2.Author = newAuthor;
            }
        }
    }
}
