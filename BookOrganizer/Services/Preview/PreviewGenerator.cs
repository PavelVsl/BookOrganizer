using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BookOrganizer.Models;
using BookOrganizer.Services.Deduplication;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Operations;
using BookOrganizer.Services.Scanning;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Preview;

/// <summary>
/// Generates previews of organization operations without executing them.
/// </summary>
public class PreviewGenerator : IPreviewGenerator
{
    private readonly ILogger<PreviewGenerator> _logger;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly IPathGenerator _pathGenerator;
    private readonly IFileOperator _fileOperator;
    private readonly IDeduplicationDetector _deduplicationDetector;

    // Platform-specific path length limits
    private const int WindowsMaxPathLength = 260;
    private const int UnixMaxPathLength = 4096;

    // Estimated transfer speed for time calculations (MB/s)
    private const double EstimatedTransferSpeedMBps = 100.0;

    public PreviewGenerator(
        ILogger<PreviewGenerator> logger,
        IDirectoryScanner directoryScanner,
        IMetadataExtractor metadataExtractor,
        IPathGenerator pathGenerator,
        IFileOperator fileOperator,
        IDeduplicationDetector deduplicationDetector)
    {
        _logger = logger;
        _directoryScanner = directoryScanner;
        _metadataExtractor = metadataExtractor;
        _pathGenerator = pathGenerator;
        _fileOperator = fileOperator;
        _deduplicationDetector = deduplicationDetector;
    }

    public async Task<PreviewResult> GeneratePreviewAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        PreviewFilter? filter = null,
        bool detectDuplicates = false,
        double duplicateThreshold = 0.7,
        bool rebuildCache = false,
        CancellationToken cancellationToken = default,
        OrganizationOptions? options = null)
    {
        var effectiveOptions = options ?? new OrganizationOptions();
        _logger.LogInformation(
            "Generating preview: {Source} -> {Destination} ({OperationType})",
            sourcePath,
            destinationPath,
            operationType);

        var stopwatch = Stopwatch.StartNew();

        // Scan source directory for audiobooks
        var audiobooks = await _directoryScanner.ScanDirectoryAsync(
            sourcePath,
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Found {Count} audiobook folders to analyze", audiobooks.Count);

        // Create organization plans for each audiobook
        var plans = new List<OrganizationPlan>();
        var audiobooksWithMetadata = new List<AudiobookWithMetadata>();

        foreach (var audiobook in audiobooks)
        {
            // Extract and consolidate metadata (with hierarchical support)
            var metadata = await _metadataExtractor.ExtractMetadataAsync(
                audiobook,
                sourcePath, // Enable hierarchical metadata detection
                cancellationToken).ConfigureAwait(false);

            // Generate target path
            var targetPath = _pathGenerator.GenerateTargetPath(
                metadata,
                destinationPath,
                effectiveOptions);

            plans.Add(new OrganizationPlan
            {
                SourceFolder = audiobook,
                Metadata = metadata,
                TargetPath = targetPath,
                OperationType = operationType
            });

            // Store for duplicate detection
            if (detectDuplicates)
            {
                audiobooksWithMetadata.Add(new AudiobookWithMetadata(audiobook, metadata));
            }
        }

        // Detect duplicates if requested
        List<DuplicationCandidate> duplicates = new();
        if (detectDuplicates && audiobooksWithMetadata.Count > 0)
        {
            _logger.LogInformation("Detecting duplicates with threshold {Threshold}", duplicateThreshold);

            // Detect duplicates within source audiobooks
            if (audiobooksWithMetadata.Count > 1)
            {
                var sourceDuplicates = await _deduplicationDetector.DetectDuplicatesAsync(
                    audiobooksWithMetadata,
                    duplicateThreshold,
                    cancellationToken).ConfigureAwait(false);

                duplicates.AddRange(sourceDuplicates);
                _logger.LogInformation("Found {Count} potential duplicates within source", sourceDuplicates.Count);
            }

            // Detect duplicates against existing library
            if (Directory.Exists(destinationPath))
            {
                var libraryDuplicates = await _deduplicationDetector.DetectDuplicatesAgainstLibraryAsync(
                    audiobooksWithMetadata,
                    destinationPath,
                    duplicateThreshold,
                    rebuildCache,
                    cancellationToken).ConfigureAwait(false);

                duplicates.AddRange(libraryDuplicates);
                _logger.LogInformation("Found {Count} potential duplicates against existing library", libraryDuplicates.Count);
            }

            _logger.LogInformation("Found {Count} total potential duplicates", duplicates.Count);
        }

        stopwatch.Stop();
        _logger.LogDebug("Preview generation took {Duration}ms", stopwatch.ElapsedMilliseconds);

        // Generate preview from plans with filtering and duplicates
        return await GeneratePreviewFromPlansAsync(plans, duplicates, filter, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<PreviewResult> GeneratePreviewFromPlansAsync(
        IEnumerable<OrganizationPlan> plans,
        CancellationToken cancellationToken = default)
    {
        return GeneratePreviewFromPlansAsync(plans, new List<DuplicationCandidate>(), null, cancellationToken);
    }

    private async Task<PreviewResult> GeneratePreviewFromPlansAsync(
        IEnumerable<OrganizationPlan> plans,
        List<DuplicationCandidate> duplicates,
        PreviewFilter? filter,
        CancellationToken cancellationToken)
    {
        var operations = new List<FileOperationPreview>();
        var allIssues = new List<PreviewIssue>();
        var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Build a map of folders to merge
        var mergeMap = BuildMergeMap(plans.ToList(), duplicates);

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Apply filters
            if (!PassesFilter(plan, filter))
            {
                continue;
            }

            // Check if this plan should be merged with another
            var targetPath = plan.TargetPath;
            var isMerged = mergeMap.TryGetValue(plan.SourceFolder.Path, out var mergedPath);
            if (isMerged)
            {
                targetPath = mergedPath;
                _logger.LogDebug(
                    "Using merged target path for '{Source}': {Target}",
                    plan.SourceFolder.Path,
                    targetPath);
            }

            // Check for path uniqueness and generate unique path if needed
            // Skip uniqueness check for merged paths - we WANT duplicates to share the same path
            if (!isMerged && existingPaths.Contains(targetPath))
            {
                targetPath = _pathGenerator.EnsureUniquePath(
                    plan.Metadata,
                    targetPath,
                    existingPaths);
            }
            existingPaths.Add(targetPath);

            // Detect issues for this operation
            var issues = await DetectIssuesAsync(plan, targetPath, cancellationToken)
                .ConfigureAwait(false);

            // Apply issue severity filter
            if (filter?.IssueSeverities != null && filter.IssueSeverities.Count > 0)
            {
                var hasMatchingIssue = issues.Any(i => filter.IssueSeverities.Contains(i.Severity));
                if (!hasMatchingIssue)
                {
                    continue;
                }
            }

            // Generate normalized names for tree grouping
            var normalizedAuthor = _pathGenerator.NormalizeAuthorName(plan.Metadata.Author ?? "");
            var normalizedTitle = _pathGenerator.SanitizePathComponent(plan.Metadata.Title ?? "");
            var normalizedSeries = string.IsNullOrWhiteSpace(plan.Metadata.Series)
                ? null
                : _pathGenerator.SanitizePathComponent(plan.Metadata.Series);

            operations.Add(new FileOperationPreview
            {
                SourceFolder = plan.SourceFolder,
                Metadata = plan.Metadata,
                SourcePath = plan.SourceFolder.Path,
                DestinationPath = targetPath,
                OperationType = plan.OperationType,
                TotalSizeBytes = plan.TotalSizeBytes,
                FileCount = plan.SourceFolder.AudioFiles.Count,
                Issues = issues,
                NormalizedAuthor = normalizedAuthor,
                NormalizedTitle = normalizedTitle,
                NormalizedSeries = normalizedSeries
            });

            allIssues.AddRange(issues);

            // Apply max items limit
            if (filter?.MaxItems.HasValue == true && operations.Count >= filter.MaxItems.Value)
            {
                break;
            }
        }

        // Add duplicate detection issues
        foreach (var duplicate in duplicates)
        {
            var severity = duplicate.MergeAutomatically ? IssueSeverity.Info : IssueSeverity.Warning;
            var actionMessage = duplicate.MergeAutomatically
                ? "Will be automatically merged into one folder"
                : $"Recommended action: {duplicate.RecommendedResolution}";

            var issue = new PreviewIssue
            {
                Severity = severity,
                Type = IssueType.PotentialDuplicate,
                Message = $"Potential duplicate: '{Path.GetFileName(duplicate.SourceFolder.Path)}' and '{Path.GetFileName(duplicate.TargetFolder.Path)}' (confidence: {duplicate.ConfidenceScore:P0})",
                SourcePath = duplicate.SourceFolder.Path,
                DestinationPath = duplicate.TargetFolder.Path,
                Suggestion = actionMessage
            };
            allIssues.Add(issue);
        }

        // Calculate statistics
        var statistics = CalculateStatistics(operations, allIssues);

        return await Task.FromResult(new PreviewResult
        {
            Operations = operations,
            Statistics = statistics,
            Issues = allIssues,
            PotentialDuplicates = duplicates,
            GeneratedAt = DateTime.UtcNow
        });
    }

    public async Task ExportPreviewAsync(
        PreviewResult preview,
        string outputPath,
        ExportFormat format,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Exporting preview to {Path} in {Format} format",
            outputPath,
            format);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        switch (format)
        {
            case ExportFormat.Json:
                await ExportAsJsonAsync(preview, outputPath, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ExportFormat.Csv:
                await ExportAsCsvAsync(preview, outputPath, cancellationToken)
                    .ConfigureAwait(false);
                break;

            case ExportFormat.Text:
                await ExportAsTextAsync(preview, outputPath, cancellationToken)
                    .ConfigureAwait(false);
                break;

            default:
                throw new ArgumentException($"Unsupported export format: {format}", nameof(format));
        }

        _logger.LogInformation("Preview exported successfully to {Path}", outputPath);
    }

    /// <summary>
    /// Builds a map of source folders to their merged target paths.
    /// Returns a dictionary where key is source folder path and value is the canonical target path to use.
    /// </summary>
    private Dictionary<string, string> BuildMergeMap(
        List<OrganizationPlan> plans,
        List<DuplicationCandidate> duplicates)
    {
        var mergeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Find duplicates that should be automatically merged
        var duplicatesToMerge = duplicates.Where(d => d.MergeAutomatically).ToList();

        if (duplicatesToMerge.Count == 0)
        {
            return mergeMap;
        }

        _logger.LogInformation(
            "Found {Count} duplicates to automatically merge",
            duplicatesToMerge.Count);

        foreach (var duplicate in duplicatesToMerge)
        {
            // Find the plans for both source and target
            var sourcePlan = plans.FirstOrDefault(p => p.SourceFolder.Path == duplicate.SourceFolder.Path);
            var targetPlan = plans.FirstOrDefault(p => p.SourceFolder.Path == duplicate.TargetFolder.Path);

            if (sourcePlan == null || targetPlan == null)
            {
                _logger.LogWarning(
                    "Could not find plans for duplicate merge: {Source} <-> {Target}",
                    duplicate.SourceFolder.Path,
                    duplicate.TargetFolder.Path);
                continue;
            }

            // Choose the canonical target path (prefer source, or remove year suffix)
            var canonicalPath = ChooseCanonicalPath(sourcePlan.TargetPath, targetPlan.TargetPath);

            // Map both source folders to the same canonical target
            mergeMap[duplicate.SourceFolder.Path] = canonicalPath;
            mergeMap[duplicate.TargetFolder.Path] = canonicalPath;

            _logger.LogInformation(
                "Will merge '{Source}' and '{Target}' into '{Canonical}'",
                Path.GetFileName(duplicate.SourceFolder.Path),
                Path.GetFileName(duplicate.TargetFolder.Path),
                canonicalPath);
        }

        return mergeMap;
    }

    /// <summary>
    /// Chooses the canonical (preferred) path from two duplicate targets.
    /// Prefers paths without year suffixes.
    /// </summary>
    private string ChooseCanonicalPath(string path1, string path2)
    {
        var name1 = Path.GetFileName(path1) ?? "";
        var name2 = Path.GetFileName(path2) ?? "";

        // Prefer path without year suffix (e.g., "Lazar" over "Lazar (2018)")
        var hasYearSuffix1 = System.Text.RegularExpressions.Regex.IsMatch(name1, @"\(\d{4}\)\s*$");
        var hasYearSuffix2 = System.Text.RegularExpressions.Regex.IsMatch(name2, @"\(\d{4}\)\s*$");

        if (hasYearSuffix1 && !hasYearSuffix2)
        {
            return path2;
        }
        if (hasYearSuffix2 && !hasYearSuffix1)
        {
            return path1;
        }

        // If both have or don't have year suffix, prefer shorter path
        return path1.Length <= path2.Length ? path1 : path2;
    }

    /// <summary>
    /// Checks if a plan passes the given filter criteria.
    /// </summary>
    private bool PassesFilter(OrganizationPlan plan, PreviewFilter? filter)
    {
        if (filter == null)
        {
            return true;
        }

        // Filter by authors
        if (filter.Authors?.Count > 0)
        {
            var hasMatchingAuthor = filter.Authors.Any(author =>
                plan.Metadata.Author?.Contains(author, StringComparison.OrdinalIgnoreCase) == true);
            if (!hasMatchingAuthor)
            {
                return false;
            }
        }

        // Filter by series
        if (filter.Series?.Count > 0)
        {
            var hasMatchingSeries = filter.Series.Any(series =>
                plan.Metadata.Series?.Contains(series, StringComparison.OrdinalIgnoreCase) == true);
            if (!hasMatchingSeries)
            {
                return false;
            }
        }

        // Filter by minimum confidence (would need to pass ConsolidatedMetadata, simplified here)
        // This is a simplified version - in real implementation we'd check field-level confidence
        if (filter.MinimumConfidence.HasValue)
        {
            // For now, we consider metadata complete if all required fields are present
            var hasCompleteMetadata = !string.IsNullOrEmpty(plan.Metadata.Author) &&
                                     !string.IsNullOrEmpty(plan.Metadata.Title);
            if (!hasCompleteMetadata && filter.MinimumConfidence.Value > 0.5)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Detects potential issues with an operation.
    /// </summary>
    private async Task<List<PreviewIssue>> DetectIssuesAsync(
        OrganizationPlan plan,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var issues = new List<PreviewIssue>();

        // Check for missing metadata
        if (string.IsNullOrEmpty(plan.Metadata.Author))
        {
            issues.Add(new PreviewIssue
            {
                Severity = IssueSeverity.Warning,
                Type = IssueType.MissingMetadata,
                Message = "Author information is missing",
                SourcePath = plan.SourceFolder.Path,
                Suggestion = "Metadata will be extracted from folder name or filename"
            });
        }

        if (string.IsNullOrEmpty(plan.Metadata.Title))
        {
            issues.Add(new PreviewIssue
            {
                Severity = IssueSeverity.Warning,
                Type = IssueType.MissingMetadata,
                Message = "Title information is missing",
                SourcePath = plan.SourceFolder.Path,
                Suggestion = "Metadata will be extracted from folder name or filename"
            });
        }

        // Check path length
        var maxPathLength = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? WindowsMaxPathLength
            : UnixMaxPathLength;

        if (targetPath.Length > maxPathLength)
        {
            issues.Add(new PreviewIssue
            {
                Severity = IssueSeverity.Error,
                Type = IssueType.PathTooLong,
                Message = $"Destination path exceeds maximum length ({targetPath.Length} > {maxPathLength})",
                DestinationPath = targetPath,
                Suggestion = "Consider shortening author/series/title names or using a shorter destination root"
            });
        }

        // Check if destination already exists
        if (Directory.Exists(targetPath))
        {
            issues.Add(new PreviewIssue
            {
                Severity = IssueSeverity.Warning,
                Type = IssueType.PathCollision,
                Message = "Destination directory already exists",
                DestinationPath = targetPath,
                Suggestion = "Files may be overwritten or merged with existing content"
            });
        }

        // Check for invalid characters in path
        var invalidChars = Path.GetInvalidPathChars();
        if (targetPath.Any(c => invalidChars.Contains(c)))
        {
            issues.Add(new PreviewIssue
            {
                Severity = IssueSeverity.Error,
                Type = IssueType.InvalidCharacters,
                Message = "Destination path contains invalid characters",
                DestinationPath = targetPath,
                Suggestion = "Path sanitization should handle this automatically"
            });
        }

        // Check if operation type is supported (e.g., hard link across volumes)
        if (plan.OperationType == FileOperationType.HardLink)
        {
            var sourceRoot = Path.GetPathRoot(plan.SourceFolder.Path);
            var destRoot = Path.GetPathRoot(targetPath);

            if (!string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new PreviewIssue
                {
                    Severity = IssueSeverity.Error,
                    Type = IssueType.UnsupportedOperation,
                    Message = "Hard links cannot span different volumes/filesystems",
                    SourcePath = plan.SourceFolder.Path,
                    DestinationPath = targetPath,
                    Suggestion = "Use Copy or SymbolicLink operation type instead"
                });
            }
        }

        // Check disk space (simplified - would need actual drive info in production)
        // For now, just add info message for copy operations
        if (plan.OperationType == FileOperationType.Copy && plan.TotalSizeBytes > 0)
        {
            issues.Add(new PreviewIssue
            {
                Severity = IssueSeverity.Info,
                Type = IssueType.Information,
                Message = $"Copy operation will use {FormatBytes(plan.TotalSizeBytes)} of disk space",
                DestinationPath = targetPath
            });
        }

        return await Task.FromResult(issues);
    }

    /// <summary>
    /// Calculates statistics for the preview.
    /// </summary>
    private PreviewStatistics CalculateStatistics(
        List<FileOperationPreview> operations,
        List<PreviewIssue> issues)
    {
        var totalFiles = operations.Sum(op => op.FileCount);
        var totalBytes = operations.Sum(op => op.TotalSizeBytes);

        // Calculate estimated disk space based on operation types
        var estimatedDiskSpace = operations
            .Where(op => op.OperationType == FileOperationType.Copy)
            .Sum(op => op.TotalSizeBytes);

        // Count operations by type
        var operationCounts = operations
            .GroupBy(op => op.OperationType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ensure all operation types are represented
        foreach (FileOperationType opType in Enum.GetValues<FileOperationType>())
        {
            if (!operationCounts.ContainsKey(opType))
            {
                operationCounts[opType] = 0;
            }
        }

        // Count issues by severity
        var issueCounts = issues
            .GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        // Ensure all severity levels are represented
        foreach (IssueSeverity severity in Enum.GetValues<IssueSeverity>())
        {
            if (!issueCounts.ContainsKey(severity))
            {
                issueCounts[severity] = 0;
            }
        }

        // Estimate duration based on copy operations (others are nearly instant)
        var copyBytes = operations
            .Where(op => op.OperationType == FileOperationType.Copy)
            .Sum(op => op.TotalSizeBytes);

        var estimatedSeconds = copyBytes / (EstimatedTransferSpeedMBps * 1024 * 1024);
        var estimatedDuration = TimeSpan.FromSeconds(estimatedSeconds);

        return new PreviewStatistics
        {
            TotalAudiobooks = operations.Count,
            TotalFiles = totalFiles,
            TotalSizeBytes = totalBytes,
            EstimatedDiskSpaceBytes = estimatedDiskSpace,
            OperationCounts = operationCounts,
            IssueCounts = issueCounts,
            EstimatedDuration = estimatedDuration
        };
    }

    /// <summary>
    /// Exports preview as JSON.
    /// </summary>
    private async Task ExportAsJsonAsync(
        PreviewResult preview,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(preview, options);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Exports preview as CSV.
    /// </summary>
    private async Task ExportAsCsvAsync(
        PreviewResult preview,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("Author,Title,Series,SeriesNumber,SourcePath,DestinationPath,OperationType,FileCount,SizeBytes,Issues");

        // Data rows
        foreach (var op in preview.Operations)
        {
            var issuesSummary = string.Join("; ", op.Issues.Select(i => $"{i.Severity}: {i.Message}"));

            sb.AppendLine(string.Join(",",
                EscapeCsv(op.Metadata.Author ?? ""),
                EscapeCsv(op.Metadata.Title ?? ""),
                EscapeCsv(op.Metadata.Series ?? ""),
                op.Metadata.SeriesNumber?.ToString() ?? "",
                EscapeCsv(op.SourcePath),
                EscapeCsv(op.DestinationPath),
                op.OperationType.ToString(),
                op.FileCount.ToString(),
                op.TotalSizeBytes.ToString(),
                EscapeCsv(issuesSummary)));
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Exports preview as plain text.
    /// </summary>
    private async Task ExportAsTextAsync(
        PreviewResult preview,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== AUDIOBOOK ORGANIZATION PREVIEW ===");
        sb.AppendLine($"Generated: {preview.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        sb.AppendLine("=== STATISTICS ===");
        sb.AppendLine($"Total Audiobooks: {preview.Statistics.TotalAudiobooks}");
        sb.AppendLine($"Total Files: {preview.Statistics.TotalFiles}");
        sb.AppendLine($"Total Size: {preview.Statistics.TotalSizeFormatted}");
        sb.AppendLine($"Estimated Disk Space: {preview.Statistics.EstimatedDiskSpaceFormatted}");
        sb.AppendLine($"Estimated Duration: {preview.Statistics.EstimatedDuration:hh\\:mm\\:ss}");
        sb.AppendLine();

        sb.AppendLine("=== OPERATIONS BY TYPE ===");
        foreach (var kvp in preview.Statistics.OperationCounts.OrderByDescending(x => x.Value))
        {
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine();

        sb.AppendLine("=== ISSUES BY SEVERITY ===");
        foreach (var kvp in preview.Statistics.IssueCounts.OrderBy(x => x.Key))
        {
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }
        sb.AppendLine();

        sb.AppendLine("=== OPERATIONS ===");
        foreach (var op in preview.Operations)
        {
            sb.AppendLine($"\n{op.Metadata.Author} - {op.Metadata.Title}");
            if (!string.IsNullOrEmpty(op.Metadata.Series))
            {
                sb.AppendLine($"  Series: {op.Metadata.Series} #{op.Metadata.SeriesNumber}");
            }
            sb.AppendLine($"  Source: {op.SourcePath}");
            sb.AppendLine($"  Destination: {op.DestinationPath}");
            sb.AppendLine($"  Operation: {op.OperationType}");
            sb.AppendLine($"  Files: {op.FileCount}, Size: {FormatBytes(op.TotalSizeBytes)}");

            if (op.Issues.Count > 0)
            {
                sb.AppendLine("  Issues:");
                foreach (var issue in op.Issues)
                {
                    sb.AppendLine($"    [{issue.Severity}] {issue.Message}");
                    if (!string.IsNullOrEmpty(issue.Suggestion))
                    {
                        sb.AppendLine($"      → {issue.Suggestion}");
                    }
                }
            }
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString(), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Escapes a string for CSV format.
    /// </summary>
    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    /// <summary>
    /// Formats bytes into human-readable string.
    /// </summary>
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
