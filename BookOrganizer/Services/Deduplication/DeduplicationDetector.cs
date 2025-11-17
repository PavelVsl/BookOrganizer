using BookOrganizer.Infrastructure.Database;
using BookOrganizer.Models;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Detects potential duplicate audiobooks using normalized metadata comparison.
/// </summary>
public class DeduplicationDetector : IDeduplicationDetector
{
    private readonly ITextNormalizer _textNormalizer;
    private readonly ContentAnalyzer _contentAnalyzer;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly ILogger<DeduplicationDetector> _logger;

    public DeduplicationDetector(
        ITextNormalizer textNormalizer,
        ContentAnalyzer contentAnalyzer,
        IDirectoryScanner directoryScanner,
        IMetadataExtractor metadataExtractor,
        ILogger<DeduplicationDetector> logger)
    {
        _textNormalizer = textNormalizer;
        _contentAnalyzer = contentAnalyzer;
        _directoryScanner = directoryScanner;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<DuplicationCandidate>> DetectDuplicatesAsync(
        IEnumerable<AudiobookWithMetadata> audiobooks,
        double confidenceThreshold = 0.7,
        CancellationToken cancellationToken = default)
    {
        var audiobooksList = audiobooks.ToList();
        var candidates = new List<DuplicationCandidate>();

        _logger.LogInformation(
            "Detecting duplicates among {Count} audiobooks (threshold: {Threshold})",
            audiobooksList.Count,
            confidenceThreshold);

        // Compare each audiobook with every other audiobook
        for (int i = 0; i < audiobooksList.Count; i++)
        {
            for (int j = i + 1; j < audiobooksList.Count; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = await CompareAudiobooksAsync(
                    audiobooksList[i],
                    audiobooksList[j],
                    cancellationToken);

                if (candidate != null && candidate.ConfidenceScore >= confidenceThreshold)
                {
                    candidates.Add(candidate);
                    _logger.LogDebug(
                        "Potential duplicate found: '{Source}' vs '{Target}' (confidence: {Confidence:F2})",
                        candidate.SourceFolder.Path,
                        candidate.TargetFolder.Path,
                        candidate.ConfidenceScore);
                }
            }
        }

        _logger.LogInformation(
            "Found {Count} potential duplicates (threshold: {Threshold})",
            candidates.Count,
            confidenceThreshold);

        return candidates;
    }

    /// <inheritdoc />
    public Task<DuplicationCandidate?> CompareAudiobooksAsync(
        AudiobookWithMetadata audiobook1,
        AudiobookWithMetadata audiobook2,
        CancellationToken cancellationToken = default)
    {
        var matchReasons = new List<string>();
        var differences = new List<string>();
        double confidenceScore = 0.0;

        var meta1 = audiobook1.Metadata;
        var meta2 = audiobook2.Metadata;
        var folder1 = audiobook1.Folder;
        var folder2 = audiobook2.Folder;

        // Compare normalized author + title (most important for duplicates)
        var authorMatch = CompareField(meta1.Author, meta2.Author);
        var titleMatch = CompareField(meta1.Title, meta2.Title);

        if (!authorMatch || !titleMatch)
        {
            // Not a duplicate if author or title don't match
            return Task.FromResult<DuplicationCandidate?>(null);
        }

        // Check for multi-part book indicators in folder names or titles
        var isMultiPart = IsMultiPartBook(folder1.Path, folder2.Path, meta1.Title, meta2.Title);
        if (isMultiPart)
        {
            // Multi-part books (Part I/II, svazek I/II, etc.) are NOT duplicates
            return Task.FromResult<DuplicationCandidate?>(null);
        }

        // Author and title match - this is likely a duplicate
        matchReasons.Add($"Author match: '{meta1.Author}' = '{meta2.Author}'");
        matchReasons.Add($"Title match: '{meta1.Title}' = '{meta2.Title}'");
        confidenceScore += 0.6; // Base confidence for author+title match

        // Compare series information
        if (!string.IsNullOrEmpty(meta1.Series) && !string.IsNullOrEmpty(meta2.Series))
        {
            if (CompareField(meta1.Series, meta2.Series))
            {
                matchReasons.Add($"Series match: '{meta1.Series}'");
                confidenceScore += 0.1;

                // Compare series number
                if (!string.IsNullOrEmpty(meta1.SeriesNumber) && !string.IsNullOrEmpty(meta2.SeriesNumber))
                {
                    if (meta1.SeriesNumber == meta2.SeriesNumber)
                    {
                        matchReasons.Add($"Series number match: {meta1.SeriesNumber}");
                        confidenceScore += 0.1;
                    }
                    else
                    {
                        differences.Add($"Different series numbers: {meta1.SeriesNumber} vs {meta2.SeriesNumber}");
                    }
                }
            }
        }

        // Compare narrator - different narrators mean different audiobook versions (keep both)
        if (!string.IsNullOrEmpty(meta1.Narrator) && !string.IsNullOrEmpty(meta2.Narrator))
        {
            if (CompareField(meta1.Narrator, meta2.Narrator))
            {
                matchReasons.Add($"Narrator match: '{meta1.Narrator}'");
                confidenceScore += 0.1;
            }
            else
            {
                // Different narrators = different audiobook versions, NOT duplicates
                differences.Add($"Different narrators: '{meta1.Narrator}' vs '{meta2.Narrator}'");
                return Task.FromResult<DuplicationCandidate?>(null);
            }
        }
        else if (!string.IsNullOrEmpty(meta1.Narrator) || !string.IsNullOrEmpty(meta2.Narrator))
        {
            differences.Add("One version has narrator information, the other doesn't");
        }

        // Compare year
        if (meta1.Year.HasValue && meta2.Year.HasValue)
        {
            if (meta1.Year == meta2.Year)
            {
                matchReasons.Add($"Year match: {meta1.Year}");
                confidenceScore += 0.05;
            }
            else
            {
                differences.Add($"Different years: {meta1.Year} vs {meta2.Year}");
            }
        }

        // Perform content analysis (duration, quality, etc.)
        var content1 = _contentAnalyzer.AnalyzeContent(folder1);
        var content2 = _contentAnalyzer.AnalyzeContent(folder2);
        var contentComparison = _contentAnalyzer.CompareContent(content1, content2);

        // Add content-based match reasons and differences
        matchReasons.AddRange(contentComparison.MatchReasons);
        differences.AddRange(contentComparison.Differences);

        // Check if file counts are significantly different (likely different versions or multi-part)
        var fileCountRatio = Math.Abs(content1.FileCount - content2.FileCount) /
                            (double)Math.Max(content1.FileCount, content2.FileCount);
        if (fileCountRatio > 0.3) // More than 30% difference in file count
        {
            differences.Add($"Significantly different file counts: {content1.FileCount} vs {content2.FileCount}");
            // Don't flag as duplicates if file structure is very different
            if (fileCountRatio > 0.5) // More than 50% difference
            {
                return Task.FromResult<DuplicationCandidate?>(null);
            }
        }

        // Check if durations are significantly different (likely abridged vs full or multi-part)
        if (contentComparison.DurationSimilarity < 0.5) // Less than 50% similar
        {
            // Very different durations suggest different versions, not duplicates
            return Task.FromResult<DuplicationCandidate?>(null);
        }

        // Adjust confidence based on content similarity
        var contentConfidence = (contentComparison.DurationSimilarity * 0.15) +
                               (contentComparison.SizeSimilarity * 0.05);
        confidenceScore += contentConfidence;

        // Determine recommended resolution based on differences
        var recommendedResolution = DetermineRecommendedResolution(
            differences,
            content1.TotalSizeBytes,
            content2.TotalSizeBytes,
            content1.TotalDuration,
            content2.TotalDuration);

        var candidate = new DuplicationCandidate
        {
            SourceFolder = folder1,
            SourceMetadata = meta1,
            TargetFolder = folder2,
            TargetMetadata = meta2,
            ConfidenceScore = Math.Max(0.0, Math.Min(1.0, confidenceScore)),
            MatchReasons = matchReasons,
            Differences = differences,
            RecommendedResolution = recommendedResolution
        };

        return Task.FromResult<DuplicationCandidate?>(candidate);
    }

    /// <inheritdoc />
    public async Task<List<DuplicationCandidate>> DetectDuplicatesAgainstLibraryAsync(
        IEnumerable<AudiobookWithMetadata> sourceAudiobooks,
        string libraryPath,
        double confidenceThreshold = 0.7,
        bool rebuildCache = false,
        CancellationToken cancellationToken = default)
    {
        var sourceList = sourceAudiobooks.ToList();
        var candidates = new List<DuplicationCandidate>();

        if (!Directory.Exists(libraryPath))
        {
            _logger.LogWarning("Library path does not exist: {Path}", libraryPath);
            return candidates;
        }

        _logger.LogInformation(
            "Detecting duplicates between {SourceCount} source audiobooks and existing library at {LibraryPath} (rebuild: {Rebuild})",
            sourceList.Count,
            libraryPath,
            rebuildCache);

        // Create and initialize library database
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var dbLogger = loggerFactory.CreateLogger<LibraryDatabase>();
        using var database = new LibraryDatabase(libraryPath, dbLogger);
        await database.InitializeAsync(cancellationToken).ConfigureAwait(false);

        // Clear library cache if rebuild requested
        if (rebuildCache)
        {
            _logger.LogInformation("Rebuilding library cache (forced rescan)");
            await database.ClearLibraryBooksAsync(cancellationToken).ConfigureAwait(false);
        }

        // Load existing library books from database
        var libraryBooks = await database.GetLibraryBooksAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Loaded {Count} books from library database", libraryBooks.Count);

        // If no library books in database, scan the library directory
        if (libraryBooks.Count == 0)
        {
            _logger.LogInformation("Library database is empty, scanning library directory...");
            var libraryFolders = await _directoryScanner.ScanDirectoryAsync(libraryPath, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Found {Count} audiobook folders in library", libraryFolders.Count);

            // Populate database with library books
            foreach (var folder in libraryFolders)
            {
                var metadata = await _metadataExtractor.ExtractMetadataAsync(folder, null, cancellationToken).ConfigureAwait(false);
                var normalizedAuthor = _textNormalizer.NormalizeForComparison(metadata.Author);
                var normalizedTitle = _textNormalizer.NormalizeForComparison(metadata.Title);
                var normalizedSeries = !string.IsNullOrEmpty(metadata.Series)
                    ? _textNormalizer.NormalizeForComparison(metadata.Series)
                    : null;

                await database.UpsertLibraryBookAsync(
                    folder,
                    metadata,
                    normalizedAuthor,
                    normalizedTitle,
                    normalizedSeries,
                    cancellationToken).ConfigureAwait(false);
            }

            // Reload library books after populating
            libraryBooks = await database.GetLibraryBooksAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Populated library database with {Count} books", libraryBooks.Count);
        }

        // Compare each source audiobook against library books
        foreach (var sourceBook in sourceList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedAuthor = _textNormalizer.NormalizeForComparison(sourceBook.Metadata.Author);
            var normalizedTitle = _textNormalizer.NormalizeForComparison(sourceBook.Metadata.Title);

            // Check if exists in library using normalized comparison
            var exists = await database.ExistsInLibraryAsync(
                normalizedAuthor,
                normalizedTitle,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (exists)
            {
                // Find matching library book(s) for detailed comparison
                var matchingLibraryBooks = libraryBooks.Where(lb =>
                    lb.NormalizedAuthor == normalizedAuthor &&
                    lb.NormalizedTitle == normalizedTitle).ToList();

                foreach (var libraryBook in matchingLibraryBooks)
                {
                    // Deserialize library book metadata
                    var libraryMetadata = JsonSerializer.Deserialize<BookMetadata>(libraryBook.MetadataJson);
                    if (libraryMetadata == null)
                    {
                        _logger.LogWarning("Failed to deserialize metadata for library book: {Path}", libraryBook.Path);
                        continue;
                    }

                    // Create AudiobookFolder for library book
                    var libraryFolder = new AudiobookFolder
                    {
                        Path = libraryBook.Path,
                        AudioFiles = new List<string>(), // Don't need files for comparison
                        TotalSizeBytes = libraryBook.SizeBytes
                    };

                    var libraryAudiobook = new AudiobookWithMetadata(libraryFolder, libraryMetadata);

                    // Compare using existing comparison logic
                    var candidate = await CompareAudiobooksAsync(
                        libraryAudiobook,
                        sourceBook,
                        cancellationToken).ConfigureAwait(false);

                    if (candidate != null && candidate.ConfidenceScore >= confidenceThreshold)
                    {
                        // Mark as duplicate against existing library
                        var libraryCandidate = candidate with { Scope = DuplicationScope.WithExistingLibrary };
                        candidates.Add(libraryCandidate);

                        _logger.LogDebug(
                            "Found duplicate against library: '{Source}' matches '{Library}' (confidence: {Confidence:F2})",
                            sourceBook.Folder.Path,
                            libraryBook.Path,
                            candidate.ConfidenceScore);
                    }
                }
            }
        }

        _logger.LogInformation(
            "Found {Count} duplicates against existing library (threshold: {Threshold})",
            candidates.Count,
            confidenceThreshold);

        return candidates;
    }

    private bool CompareField(string? field1, string? field2)
    {
        // Use text normalizer for Czech-aware comparison
        return _textNormalizer.AreEquivalent(field1, field2);
    }

    /// <summary>
    /// Detects if two audiobooks are parts of a multi-part book series.
    /// Checks for indicators like "I/II", "Part 1/2", "svazek I/II", "díl 1/2", etc.
    /// </summary>
    private bool IsMultiPartBook(string path1, string path2, string? title1, string? title2)
    {
        // Common multi-part indicators (case-insensitive)
        // Czech: svazek, díl, část
        // English: part, volume, vol, book
        // Roman numerals: I, II, III, IV, V, VI, VII, VIII, IX, X
        // Numbers: 1, 2, 3, etc.

        var text1 = $"{Path.GetFileName(path1)} {title1}".ToUpperInvariant();
        var text2 = $"{Path.GetFileName(path2)} {title2}".ToUpperInvariant();

        // Patterns for multi-part indicators
        var patterns = new[]
        {
            // Roman numerals at end or with separators
            @"\bI\b.*\bII\b", @"\bII\b.*\bI\b",
            @"\bI\b.*\bIII\b", @"\bIII\b.*\bI\b",
            @"\bII\b.*\bIII\b", @"\bIII\b.*\bII\b",
            @"\bIV\b.*\bV\b", @"\bV\b.*\bIV\b",

            // Czech indicators
            @"SVAZEK\s*\d+.*SVAZEK\s*\d+",
            @"DÍL\s*\d+.*DÍL\s*\d+",
            @"ČÁST\s*\d+.*ČÁST\s*\d+",

            // English indicators
            @"PART\s*\d+.*PART\s*\d+",
            @"VOLUME\s*\d+.*VOLUME\s*\d+",
            @"VOL\.?\s*\d+.*VOL\.?\s*\d+",
            @"BOOK\s*\d+.*BOOK\s*\d+",

            // Number patterns (e.g., "lazar 1" vs "lazar 2")
            @"\s+\d+\s*$", // Number at end of string
        };

        var combinedText = $"{text1}|{text2}";

        foreach (var pattern in patterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(combinedText, pattern))
            {
                _logger.LogDebug(
                    "Detected multi-part book pattern '{Pattern}' in: '{Path1}' vs '{Path2}'",
                    pattern,
                    path1,
                    path2);
                return true;
            }
        }

        // Check if one path contains part indicator and they're different
        var partIndicators = new[] { " I", " II", " III", " IV", " V", " 1", " 2", " 3", " 4", " 5",
                                     "SVAZEK", "DÍL", "ČÁST", "PART", "VOLUME", "VOL" };

        var hasIndicator1 = partIndicators.Any(ind => text1.Contains(ind));
        var hasIndicator2 = partIndicators.Any(ind => text2.Contains(ind));

        if (hasIndicator1 && hasIndicator2)
        {
            // Both have indicators - check if they're different
            foreach (var indicator in partIndicators)
            {
                var pos1 = text1.IndexOf(indicator, StringComparison.Ordinal);
                var pos2 = text2.IndexOf(indicator, StringComparison.Ordinal);

                if (pos1 >= 0 && pos2 >= 0)
                {
                    // Get text after the indicator to compare
                    var suffix1 = text1.Substring(pos1);
                    var suffix2 = text2.Substring(pos2);

                    if (suffix1 != suffix2)
                    {
                        _logger.LogDebug(
                            "Detected different part indicators: '{Suffix1}' vs '{Suffix2}'",
                            suffix1,
                            suffix2);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static DuplicationResolution DetermineRecommendedResolution(
        List<string> differences,
        long size1,
        long size2,
        TimeSpan duration1,
        TimeSpan duration2)
    {
        // If no meaningful differences, merge them
        if (differences.Count == 0)
        {
            return DuplicationResolution.Merge;
        }

        // If significantly different durations (>50%), likely abridged vs full
        var durationRatio = Math.Abs((duration1 - duration2).TotalMinutes) /
                           Math.Max(duration1.TotalMinutes, duration2.TotalMinutes);
        if (durationRatio > 0.5)
        {
            return DuplicationResolution.KeepBoth; // Different versions
        }

        // If significant size difference, recommend keeping both
        var sizeRatio = Math.Abs(size1 - size2) / (double)Math.Max(size1, size2);
        if (sizeRatio > 0.5)
        {
            return DuplicationResolution.KeepBoth; // Likely different versions
        }

        // If different narrators, recommend keeping both
        if (differences.Any(d => d.Contains("narrator", StringComparison.OrdinalIgnoreCase)))
        {
            return DuplicationResolution.KeepBoth;
        }

        // Minor differences (e.g., only year) - merge them into one folder
        if (differences.Count <= 2)
        {
            return DuplicationResolution.Merge;
        }

        // Otherwise, keep the larger/longer version (higher quality)
        if (duration1 > duration2 || size1 > size2)
            return DuplicationResolution.KeepSource;

        return DuplicationResolution.KeepTarget;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
