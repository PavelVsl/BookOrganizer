using BookOrganizer.Models;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Detects potential duplicate audiobooks using normalized metadata comparison.
/// </summary>
public class DeduplicationDetector : IDeduplicationDetector
{
    private readonly ITextNormalizer _textNormalizer;
    private readonly ILogger<DeduplicationDetector> _logger;

    public DeduplicationDetector(
        ITextNormalizer textNormalizer,
        ILogger<DeduplicationDetector> logger)
    {
        _textNormalizer = textNormalizer;
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

        // Compare narrator
        if (!string.IsNullOrEmpty(meta1.Narrator) && !string.IsNullOrEmpty(meta2.Narrator))
        {
            if (CompareField(meta1.Narrator, meta2.Narrator))
            {
                matchReasons.Add($"Narrator match: '{meta1.Narrator}'");
                confidenceScore += 0.1;
            }
            else
            {
                differences.Add($"Different narrators: '{meta1.Narrator}' vs '{meta2.Narrator}'");
                confidenceScore -= 0.05; // Different narrators reduce confidence slightly
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

        // Compare file counts
        if (folder1.AudioFiles.Count != folder2.AudioFiles.Count)
        {
            differences.Add($"Different file counts: {folder1.AudioFiles.Count} vs {folder2.AudioFiles.Count} files");
        }

        // Compare total file sizes
        var size1 = folder1.AudioFiles.Sum(f => new FileInfo(f).Length);
        var size2 = folder2.AudioFiles.Sum(f => new FileInfo(f).Length);
        var sizeDiff = Math.Abs(size1 - size2);
        var sizeRatio = sizeDiff / (double)Math.Max(size1, size2);

        if (sizeRatio < 0.05) // Less than 5% difference
        {
            matchReasons.Add($"Similar sizes: {FormatBytes(size1)} vs {FormatBytes(size2)}");
            confidenceScore += 0.1;
        }
        else if (sizeRatio < 0.5) // 5-50% difference
        {
            differences.Add($"Different sizes: {FormatBytes(size1)} vs {FormatBytes(size2)} ({sizeRatio:P0} difference)");
        }
        else
        {
            // More than 50% size difference - likely abridged vs full version
            differences.Add($"Significant size difference: {FormatBytes(size1)} vs {FormatBytes(size2)} ({sizeRatio:P0} difference) - possibly abridged vs full");
            confidenceScore -= 0.1;
        }

        // Determine recommended resolution based on differences
        var recommendedResolution = DetermineRecommendedResolution(differences, size1, size2);

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

    private bool CompareField(string? field1, string? field2)
    {
        // Use text normalizer for Czech-aware comparison
        return _textNormalizer.AreEquivalent(field1, field2);
    }

    private static DuplicationResolution DetermineRecommendedResolution(List<string> differences, long size1, long size2)
    {
        // If no meaningful differences, recommend keeping source
        if (differences.Count == 0)
        {
            return DuplicationResolution.KeepSource;
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

        // Otherwise, keep the larger version (higher quality)
        return size1 >= size2 ? DuplicationResolution.KeepSource : DuplicationResolution.KeepTarget;
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
