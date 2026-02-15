using BookOrganizer.Models;
using Microsoft.Extensions.Logging;
using TagLib;

namespace BookOrganizer.Services.Deduplication;

/// <summary>
/// Analyzes audiobook content (duration, bitrate, quality) for comparison.
/// </summary>
public class ContentAnalyzer
{
    private readonly ILogger<ContentAnalyzer> _logger;

    public ContentAnalyzer(ILogger<ContentAnalyzer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes audio content of an audiobook folder.
    /// </summary>
    public ContentAnalysis AnalyzeContent(AudiobookFolder folder)
    {
        var durations = new List<TimeSpan>();
        var bitrates = new List<int>();
        var sampleRates = new List<int>();
        long totalSize = 0;

        foreach (var filePath in folder.AudioFiles)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);

                if (file.Properties != null)
                {
                    durations.Add(file.Properties.Duration);

                    if (file.Properties.AudioBitrate > 0)
                        bitrates.Add(file.Properties.AudioBitrate);

                    if (file.Properties.AudioSampleRate > 0)
                        sampleRates.Add(file.Properties.AudioSampleRate);
                }

                totalSize += new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze audio file: {Path}", filePath);
            }
        }

        var totalDuration = TimeSpan.FromTicks(durations.Sum(d => d.Ticks));
        var avgBitrate = bitrates.Count > 0 ? (int)bitrates.Average() : 0;
        var avgSampleRate = sampleRates.Count > 0 ? (int)sampleRates.Average() : 0;

        return new ContentAnalysis
        {
            TotalDuration = totalDuration,
            FileCount = folder.AudioFiles.Count,
            TotalSizeBytes = totalSize,
            AverageBitrate = avgBitrate,
            AverageSampleRate = avgSampleRate
        };
    }

    /// <summary>
    /// Compares two content analyses and returns similarity metrics.
    /// </summary>
    public ContentComparison CompareContent(ContentAnalysis content1, ContentAnalysis content2)
    {
        var reasons = new List<string>();
        var differences = new List<string>();

        // Compare durations
        var durationDiff = Math.Abs((content1.TotalDuration - content2.TotalDuration).TotalMinutes);
        var durationRatio = durationDiff / Math.Max(content1.TotalDuration.TotalMinutes, content2.TotalDuration.TotalMinutes);

        if (durationRatio < 0.05) // Less than 5% difference
        {
            reasons.Add($"Similar duration: {FormatDuration(content1.TotalDuration)} vs {FormatDuration(content2.TotalDuration)}");
        }
        else if (durationRatio < 0.5) // 5-50% difference
        {
            differences.Add($"Different durations: {FormatDuration(content1.TotalDuration)} vs {FormatDuration(content2.TotalDuration)} ({durationRatio:P0} difference)");
        }
        else
        {
            differences.Add($"Significantly different durations: {FormatDuration(content1.TotalDuration)} vs {FormatDuration(content2.TotalDuration)} - likely abridged vs full");
        }

        // Compare file counts
        if (content1.FileCount != content2.FileCount)
        {
            differences.Add($"Different file counts: {content1.FileCount} vs {content2.FileCount}");
        }

        // Compare bitrates (quality indicator)
        if (content1.AverageBitrate > 0 && content2.AverageBitrate > 0)
        {
            var bitrateDiff = Math.Abs(content1.AverageBitrate - content2.AverageBitrate);
            var bitrateRatio = bitrateDiff / (double)Math.Max(content1.AverageBitrate, content2.AverageBitrate);

            if (bitrateRatio > 0.2) // More than 20% difference
            {
                differences.Add($"Different quality: {content1.AverageBitrate}kbps vs {content2.AverageBitrate}kbps");
            }
        }

        // Compare sample rates
        if (content1.AverageSampleRate > 0 && content2.AverageSampleRate > 0)
        {
            if (content1.AverageSampleRate != content2.AverageSampleRate)
            {
                differences.Add($"Different sample rates: {content1.AverageSampleRate}Hz vs {content2.AverageSampleRate}Hz");
            }
        }

        return new ContentComparison
        {
            DurationSimilarity = 1.0 - Math.Min(1.0, durationRatio),
            SizeSimilarity = CalculateSizeSimilarity(content1.TotalSizeBytes, content2.TotalSizeBytes),
            QualitySimilarity = CalculateQualitySimilarity(content1.AverageBitrate, content2.AverageBitrate),
            MatchReasons = reasons,
            Differences = differences
        };
    }

    private static double CalculateSizeSimilarity(long size1, long size2)
    {
        var diff = Math.Abs(size1 - size2);
        var ratio = diff / (double)Math.Max(size1, size2);
        return 1.0 - Math.Min(1.0, ratio);
    }

    private static double CalculateQualitySimilarity(int bitrate1, int bitrate2)
    {
        if (bitrate1 == 0 || bitrate2 == 0)
            return 0.5; // Unknown

        var diff = Math.Abs(bitrate1 - bitrate2);
        var ratio = diff / (double)Math.Max(bitrate1, bitrate2);
        return 1.0 - Math.Min(1.0, ratio);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";

        return $"{duration.Minutes}m {duration.Seconds}s";
    }
}

/// <summary>
/// Represents analyzed content of an audiobook.
/// </summary>
public record ContentAnalysis
{
    public TimeSpan TotalDuration { get; init; }
    public int FileCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public int AverageBitrate { get; init; }
    public int AverageSampleRate { get; init; }
}

/// <summary>
/// Represents a comparison between two audiobook contents.
/// </summary>
public record ContentComparison
{
    /// <summary>
    /// Similarity score for duration (0.0 = very different, 1.0 = identical).
    /// </summary>
    public double DurationSimilarity { get; init; }

    /// <summary>
    /// Similarity score for file size (0.0 = very different, 1.0 = identical).
    /// </summary>
    public double SizeSimilarity { get; init; }

    /// <summary>
    /// Similarity score for audio quality (0.0 = very different, 1.0 = identical).
    /// </summary>
    public double QualitySimilarity { get; init; }

    /// <summary>
    /// Reasons why content is similar.
    /// </summary>
    public List<string> MatchReasons { get; init; } = new();

    /// <summary>
    /// Detected differences between content.
    /// </summary>
    public List<string> Differences { get; init; } = new();
}
