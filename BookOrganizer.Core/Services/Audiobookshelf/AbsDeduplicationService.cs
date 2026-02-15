using BookOrganizer.Models;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Audiobookshelf;

/// <summary>
/// Compares source audiobooks against Audiobookshelf library items for deduplication.
/// </summary>
public class AbsDeduplicationService
{
    private readonly ITextNormalizer _textNormalizer;
    private readonly ILogger<AbsDeduplicationService> _logger;

    public AbsDeduplicationService(
        ITextNormalizer textNormalizer,
        ILogger<AbsDeduplicationService> logger)
    {
        _textNormalizer = textNormalizer;
        _logger = logger;
    }

    /// <summary>
    /// Finds source audiobooks that already exist in the ABS library.
    /// Matches on title (required) and author (if available).
    /// </summary>
    public List<AbsDuplicateMatch> FindDuplicates(
        IReadOnlyList<(string SourcePath, BookMetadata Metadata)> sourceBooks,
        IReadOnlyList<AbsLibraryItem> absItems)
    {
        var matches = new List<AbsDuplicateMatch>();

        _logger.LogInformation(
            "Checking {SourceCount} source books against {AbsCount} ABS items",
            sourceBooks.Count, absItems.Count);

        foreach (var (sourcePath, metadata) in sourceBooks)
        {
            var sourceTitle = metadata.Title;
            if (string.IsNullOrWhiteSpace(sourceTitle))
                continue;

            foreach (var absItem in absItems)
            {
                var absTitle = absItem.Media?.Metadata?.Title;
                if (string.IsNullOrWhiteSpace(absTitle))
                    continue;

                var titleMatch = _textNormalizer.AreEquivalent(sourceTitle, absTitle);
                if (!titleMatch)
                    continue;

                // Title matches — check author if both have one
                var sourceAuthor = metadata.Author;
                var absAuthor = absItem.Media?.Metadata?.AuthorName;
                var authorMatch = false;

                if (!string.IsNullOrWhiteSpace(sourceAuthor) && !string.IsNullOrWhiteSpace(absAuthor))
                {
                    authorMatch = _textNormalizer.AreEquivalent(sourceAuthor, absAuthor);
                    // If both have authors but they don't match, skip
                    if (!authorMatch)
                        continue;
                }
                else
                {
                    // If either is missing an author, title match alone is enough
                    authorMatch = string.IsNullOrWhiteSpace(sourceAuthor) && string.IsNullOrWhiteSpace(absAuthor);
                }

                matches.Add(new AbsDuplicateMatch
                {
                    SourcePath = sourcePath,
                    SourceTitle = sourceTitle,
                    SourceAuthor = sourceAuthor,
                    AbsItemId = absItem.Id,
                    AbsTitle = absTitle,
                    AbsAuthor = absAuthor,
                    TitleMatch = titleMatch,
                    AuthorMatch = authorMatch
                });

                _logger.LogDebug(
                    "ABS duplicate found: '{SourceTitle}' by '{SourceAuthor}' matches ABS '{AbsTitle}' by '{AbsAuthor}'",
                    sourceTitle, sourceAuthor, absTitle, absAuthor);

                break; // One match per source book is enough
            }
        }

        _logger.LogInformation("Found {Count} ABS duplicates", matches.Count);
        return matches;
    }

    /// <summary>
    /// Applies the chosen duplicate action to source folders.
    /// </summary>
    public void ApplyDuplicateAction(
        IReadOnlyList<AbsDuplicateMatch> duplicates,
        string sourceRoot,
        AbsDuplicateAction action)
    {
        if (duplicates.Count == 0 || action == AbsDuplicateAction.Skip)
            return;

        foreach (var dup in duplicates)
        {
            var sourcePath = dup.SourcePath;
            if (!Directory.Exists(sourcePath))
                continue;

            switch (action)
            {
                case AbsDuplicateAction.Rename:
                {
                    var folderName = Path.GetFileName(sourcePath);
                    var parentDir = Path.GetDirectoryName(sourcePath)!;
                    var newName = $"_DUP_{folderName}";
                    var newPath = Path.Combine(parentDir, newName);
                    if (!Directory.Exists(newPath))
                    {
                        Directory.Move(sourcePath, newPath);
                        _logger.LogInformation("Renamed duplicate: {Old} -> {New}", sourcePath, newPath);
                    }
                    break;
                }

                case AbsDuplicateAction.Move:
                {
                    var duplicatesDir = Path.Combine(sourceRoot, "_duplicates");
                    Directory.CreateDirectory(duplicatesDir);
                    var folderName = Path.GetFileName(sourcePath);
                    var newPath = Path.Combine(duplicatesDir, folderName);
                    if (!Directory.Exists(newPath))
                    {
                        Directory.Move(sourcePath, newPath);
                        _logger.LogInformation("Moved duplicate to _duplicates/: {Path}", folderName);
                    }
                    break;
                }

                case AbsDuplicateAction.Delete:
                {
                    Directory.Delete(sourcePath, recursive: true);
                    _logger.LogInformation("Deleted duplicate source: {Path}", sourcePath);
                    break;
                }
            }
        }
    }
}
