using System.Diagnostics;
using System.Text;
using BookOrganizer.Models;
using BookOrganizer.Services.Deduplication;
using BookOrganizer.Services.Metadata;
using BookOrganizer.Services.Scanning;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Organizes audiobook files by orchestrating scanning, metadata extraction, and file operations.
/// </summary>
public class FileOrganizer : IFileOrganizer
{
    private readonly ILogger<FileOrganizer> _logger;
    private readonly IDirectoryScanner _directoryScanner;
    private readonly IMetadataExtractor _metadataExtractor;
    private readonly IPathGenerator _pathGenerator;
    private readonly IFileOperator _fileOperator;
    private readonly IFilenameNormalizer _filenameNormalizer;
    private readonly IDeduplicationDetector _deduplicationDetector;
    private readonly NfoFormatter _nfoFormatter;

    public FileOrganizer(
        ILogger<FileOrganizer> logger,
        IDirectoryScanner directoryScanner,
        IMetadataExtractor metadataExtractor,
        IPathGenerator pathGenerator,
        IFileOperator fileOperator,
        IFilenameNormalizer filenameNormalizer,
        IDeduplicationDetector deduplicationDetector,
        NfoFormatter nfoFormatter)
    {
        _logger = logger;
        _directoryScanner = directoryScanner;
        _metadataExtractor = metadataExtractor;
        _pathGenerator = pathGenerator;
        _fileOperator = fileOperator;
        _filenameNormalizer = filenameNormalizer;
        _deduplicationDetector = deduplicationDetector;
        _nfoFormatter = nfoFormatter;
    }

    public async Task<OrganizationResult> OrganizeAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        bool validateIntegrity = true,
        bool detectDuplicates = false,
        double duplicateThreshold = 0.7,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        OrganizationOptions? options = null)
    {
        var effectiveOptions = options ?? new OrganizationOptions();

        _logger.LogInformation(
            "Starting organization: {Source} -> {Destination} ({OperationType}, PreserveDiacritics={PreserveDiacritics})",
            sourcePath,
            destinationPath,
            operationType,
            effectiveOptions.PreserveDiacritics);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Scan source directory
            _logger.LogInformation("Scanning source directory: {Path}", sourcePath);
            var audiobooks = await _directoryScanner.ScanDirectoryAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Found {Count} audiobook folders", audiobooks.Count);

            // Create organization plans
            var plans = new List<OrganizationPlan>();
            var audiobooksWithMetadata = new List<AudiobookWithMetadata>();

            foreach (var audiobook in audiobooks)
            {
                // Extract metadata
                var metadata = await _metadataExtractor.ExtractMetadataAsync(audiobook, null, cancellationToken)
                    .ConfigureAwait(false);

                // Generate target path
                var targetPath = _pathGenerator.GenerateTargetPath(metadata, destinationPath, effectiveOptions);

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
            if (detectDuplicates && audiobooksWithMetadata.Count > 1)
            {
                _logger.LogInformation("Detecting duplicates with threshold {Threshold}", duplicateThreshold);
                duplicates = await _deduplicationDetector.DetectDuplicatesAsync(
                    audiobooksWithMetadata,
                    duplicateThreshold,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Found {Count} potential duplicates", duplicates.Count);
            }

            // Build merge map for automatic duplicate handling
            var mergeMap = BuildMergeMap(plans, duplicates);

            // Apply merge map and ensure unique paths
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                var targetPath = plan.TargetPath;

                // Check if this plan should be merged with another
                if (mergeMap.TryGetValue(plan.SourceFolder.Path, out var mergedPath))
                {
                    targetPath = mergedPath;
                    _logger.LogDebug(
                        "Using merged target path for '{Source}': {Target}",
                        plan.SourceFolder.Path,
                        targetPath);
                }

                // Ensure unique path (skip for merged paths - we WANT duplicates to share the same path)
                var isMerged = mergeMap.ContainsKey(plan.SourceFolder.Path);
                if (!isMerged && existingPaths.Contains(targetPath))
                {
                    targetPath = _pathGenerator.EnsureUniquePath(plan.Metadata, targetPath, existingPaths);
                }
                existingPaths.Add(targetPath);

                // Update plan with potentially modified target path
                plans[i] = plan with { TargetPath = targetPath };
            }

            // Execute plans
            var result = await OrganizeFromPlansAsync(plans, validateIntegrity, progress, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation(
                "Organization completed in {Duration}. Success: {SuccessCount}/{Total}",
                stopwatch.Elapsed,
                result.SuccessfulAudiobooks,
                result.TotalAudiobooks);

            return result with { Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Organization failed");

            return new OrganizationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                AudiobookResults = Array.Empty<AudiobookOperationResult>()
            };
        }
    }

    public async Task<OrganizationResult> OrganizeFromPlansAsync(
        IEnumerable<OrganizationPlan> plans,
        bool validateIntegrity = true,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var plansList = plans.ToList();
        var results = new List<AudiobookOperationResult>();
        var stopwatch = Stopwatch.StartNew();

        var totalAudiobooks = plansList.Count;
        var totalFiles = plansList.Sum(p => p.SourceFolder.AudioFiles.Count);
        var audiobooksCompleted = 0;
        var filesCompleted = 0;

        _logger.LogInformation(
            "Executing {AudiobookCount} organization plans ({FileCount} files total)",
            totalAudiobooks,
            totalFiles);

        foreach (var plan in plansList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Processing audiobook: {Author} - {Title}",
                plan.Metadata.Author ?? "Unknown",
                plan.Metadata.Title);

            try
            {
                // Create progress wrapper for file operations
                var fileProgress = progress != null
                    ? new Progress<FileOperationProgress>(fileOp =>
                    {
                        progress.Report(new OrganizationProgress
                        {
                            CurrentAudiobook = $"{plan.Metadata.Author} - {plan.Metadata.Title}",
                            CurrentFile = fileOp.CurrentFile,
                            AudiobooksCompleted = audiobooksCompleted,
                            TotalAudiobooks = totalAudiobooks,
                            FilesCompleted = filesCompleted,
                            TotalFiles = totalFiles,
                            Stage = fileOp.Stage
                        });
                    })
                    : null;

                var result = await OrganizeSingleAudiobookAsync(
                    plan,
                    validateIntegrity,
                    fileProgress,
                    cancellationToken).ConfigureAwait(false);

                results.Add(result);
                filesCompleted += result.FilesProcessed;
                audiobooksCompleted++;

                // Report completion of this audiobook
                progress?.Report(new OrganizationProgress
                {
                    CurrentAudiobook = $"{plan.Metadata.Author} - {plan.Metadata.Title}",
                    AudiobooksCompleted = audiobooksCompleted,
                    TotalAudiobooks = totalAudiobooks,
                    FilesCompleted = filesCompleted,
                    TotalFiles = totalFiles,
                    Stage = OperationStage.Completed
                });

                _logger.LogInformation(
                    "Successfully organized: {Path} ({FilesCount} files)",
                    plan.TargetPath,
                    result.FilesProcessed);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to organize audiobook: {Source}",
                    plan.SourceFolder.Path);

                results.Add(new AudiobookOperationResult
                {
                    SourceFolder = plan.SourceFolder,
                    Metadata = plan.Metadata,
                    TargetPath = plan.TargetPath,
                    Success = false,
                    ErrorMessage = ex.Message
                });

                audiobooksCompleted++;
            }
        }

        stopwatch.Stop();

        var successful = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success);
        var totalBytesProcessed = results.Sum(r => r.SourceFolder.TotalSizeBytes);

        return new OrganizationResult
        {
            Success = failed == 0,
            TotalAudiobooks = totalAudiobooks,
            SuccessfulAudiobooks = successful,
            FailedAudiobooks = failed,
            TotalFiles = filesCompleted,
            TotalBytesProcessed = totalBytesProcessed,
            Duration = stopwatch.Elapsed,
            AudiobookResults = results,
            ErrorMessage = failed > 0 ? $"{failed} audiobook(s) failed to organize" : null
        };
    }

    /// <summary>
    /// Organizes a single audiobook by copying/moving all its files.
    /// For multi-disc audiobooks, preserves disc subfolder structure.
    /// </summary>
    private async Task<AudiobookOperationResult> OrganizeSingleAudiobookAsync(
        OrganizationPlan plan,
        bool validateIntegrity,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Create destination directory
        Directory.CreateDirectory(plan.TargetPath);

        var filesProcessed = 0;
        var filesFailed = 0;
        var isMultiDisc = plan.SourceFolder.IsMultiDisc;

        // Process all files (audio + cover images/metadata)
        foreach (var sourceFile in plan.SourceFolder.AllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string destinationFile;

                if (isMultiDisc)
                {
                    // Preserve relative path (disc subfolder structure)
                    var relativePath = Path.GetRelativePath(plan.SourceFolder.Path, sourceFile);
                    destinationFile = Path.Combine(plan.TargetPath, relativePath);

                    // Ensure the subdirectory exists
                    var destDir = Path.GetDirectoryName(destinationFile);
                    if (destDir != null)
                    {
                        Directory.CreateDirectory(destDir);
                    }
                }
                else
                {
                    // Flatten into target directory with filename normalization
                    var fileName = Path.GetFileName(sourceFile);
                    var normalizedFileName = _filenameNormalizer.NormalizeFilename(fileName);
                    destinationFile = Path.Combine(plan.TargetPath, normalizedFileName);
                }

                _logger.LogDebug(
                    "Processing file: {Source} -> {Destination}",
                    sourceFile,
                    destinationFile);

                var result = await _fileOperator.ExecuteFileOperationAsync(
                    sourceFile,
                    destinationFile,
                    plan.OperationType,
                    validateIntegrity,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (result.Success)
                {
                    filesProcessed++;
                }
                else
                {
                    filesFailed++;
                    _logger.LogWarning(
                        "File operation failed: {Source} - {Error}",
                        sourceFile,
                        result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                filesFailed++;
                _logger.LogError(
                    ex,
                    "Failed to process file: {File}",
                    sourceFile);

                // Continue with next file instead of failing entire audiobook
            }
        }

        // Auto-write metadata.nfo if no files failed and it doesn't already exist
        if (filesFailed == 0)
        {
            await WriteNfoIfNeededAsync(plan.TargetPath, plan.Metadata);
        }

        var success = filesFailed == 0;

        return new AudiobookOperationResult
        {
            SourceFolder = plan.SourceFolder,
            Metadata = plan.Metadata,
            TargetPath = plan.TargetPath,
            Success = success,
            FilesProcessed = filesProcessed,
            FilesFailed = filesFailed,
            ErrorMessage = filesFailed > 0
                ? $"{filesFailed} file(s) failed to process"
                : null
        };
    }

    /// <summary>
    /// Writes metadata.nfo to the target folder if one doesn't already exist.
    /// Does not overwrite existing NFO files (they may be manually curated).
    /// </summary>
    private async Task WriteNfoIfNeededAsync(string targetPath, BookMetadata metadata)
    {
        var nfoPath = Path.Combine(targetPath, _nfoFormatter.FileName);

        if (System.IO.File.Exists(nfoPath))
        {
            _logger.LogDebug("Skipping NFO write - file already exists: {Path}", nfoPath);
            return;
        }

        try
        {
            var nfoContent = await _nfoFormatter.FormatAsync(metadata);
            await System.IO.File.WriteAllTextAsync(nfoPath, nfoContent, Encoding.UTF8);
            _logger.LogInformation("Wrote metadata.nfo: {Path}", nfoPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write metadata.nfo: {Path}", nfoPath);
        }
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

    public async Task<OrganizationResult> ReorganizeLibraryAsync(
        string libraryPath,
        bool validateIntegrity = true,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default,
        OrganizationOptions? options = null)
    {
        var effectiveOptions = options ?? new OrganizationOptions();

        _logger.LogInformation("Starting library reorganization: {LibraryPath} (PreserveDiacritics={PreserveDiacritics})",
            libraryPath, effectiveOptions.PreserveDiacritics);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Scan library directory
            _logger.LogInformation("Scanning library directory: {Path}", libraryPath);
            var audiobooks = await _directoryScanner.ScanDirectoryAsync(libraryPath, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Found {Count} audiobook folders in library", audiobooks.Count);

            // Create reorganization plans by comparing current vs expected paths
            var plans = new List<OrganizationPlan>();

            foreach (var audiobook in audiobooks)
            {
                // Extract metadata (prioritizes metadata.json with hierarchical support)
                var metadata = await _metadataExtractor.ExtractMetadataAsync(
                    audiobook,
                    libraryPath,  // Enable hierarchical metadata loading
                    cancellationToken).ConfigureAwait(false);

                // Generate what the path SHOULD be based on current metadata
                var expectedPath = _pathGenerator.GenerateTargetPath(metadata, libraryPath, effectiveOptions);

                // If path differs from current location, needs reorganization
                if (!string.Equals(audiobook.Path, expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Book needs reorganization: '{CurrentPath}' -> '{ExpectedPath}'",
                        audiobook.Path,
                        expectedPath);

                    plans.Add(new OrganizationPlan
                    {
                        SourceFolder = audiobook,
                        Metadata = metadata,
                        TargetPath = expectedPath,
                        OperationType = FileOperationType.Move // Always use Move for reorganization
                    });
                }
            }

            if (plans.Count == 0)
            {
                _logger.LogInformation("No reorganization needed - all books are already in correct locations");

                // Still clean up orphaned directories even when no books need moving
                await CleanupEmptyDirectoriesAsync(libraryPath);

                return new OrganizationResult
                {
                    Success = true,
                    TotalAudiobooks = audiobooks.Count,
                    SuccessfulAudiobooks = audiobooks.Count,
                    FailedAudiobooks = 0,
                    TotalFiles = 0,
                    TotalBytesProcessed = 0,
                    Duration = stopwatch.Elapsed,
                    AudiobookResults = audiobooks.Select(ab => new AudiobookOperationResult
                    {
                        SourceFolder = ab,
                        Metadata = new BookMetadata { Title = Path.GetFileName(ab.Path), Source = "NoChange" },
                        TargetPath = ab.Path,
                        Success = true,
                        FilesProcessed = 0,
                        FilesFailed = 0
                    }).ToList()
                };
            }

            _logger.LogInformation("Found {Count} books requiring reorganization", plans.Count);

            // Ensure unique paths (handle collisions)
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];
                var targetPath = plan.TargetPath;

                // Ensure unique path
                if (existingPaths.Contains(targetPath))
                {
                    targetPath = _pathGenerator.EnsureUniquePath(plan.Metadata, targetPath, existingPaths);
                    _logger.LogWarning(
                        "Path collision detected, using unique path: {Path}",
                        targetPath);
                }
                existingPaths.Add(targetPath);

                // Update plan with potentially modified target path
                plans[i] = plan with { TargetPath = targetPath };
            }

            // Execute reorganization plans
            var result = await OrganizeFromPlansAsync(plans, validateIntegrity, progress, cancellationToken)
                .ConfigureAwait(false);

            // Clean up empty directories
            await CleanupEmptyDirectoriesAsync(libraryPath);

            stopwatch.Stop();
            _logger.LogInformation(
                "Reorganization completed in {Duration}. Success: {SuccessCount}/{Total}",
                stopwatch.Elapsed,
                result.SuccessfulAudiobooks,
                result.TotalAudiobooks);

            return result with { Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Reorganization failed");

            return new OrganizationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed,
                AudiobookResults = Array.Empty<AudiobookOperationResult>()
            };
        }
    }

    // Files considered expendable during cleanup (generated metadata + OS junk)
    private static readonly HashSet<string> ExpendableFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bookinfo.json",
        "metadata.json",
        "metadata.nfo",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini"
    };

    /// <summary>
    /// Cleans up directories left after reorganization that are empty or contain only metadata files.
    /// Repeats until no more directories can be removed (handles nested cleanup).
    /// </summary>
    public async Task CleanupEmptyDirectoriesAsync(string libraryPath)
    {
        await Task.Run(() =>
        {
            try
            {
                bool removedAny;
                do
                {
                    removedAny = false;

                    // Re-scan each pass since parent dirs may become removable after children are deleted
                    var directories = Directory.GetDirectories(libraryPath, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length); // Process deepest first

                    foreach (var directory in directories)
                    {
                        try
                        {
                            if (!Directory.Exists(directory))
                                continue;

                            // Skip if directory has subdirectories
                            if (Directory.EnumerateDirectories(directory).Any())
                                continue;

                            var files = Directory.GetFiles(directory);

                            // Empty directory - delete
                            if (files.Length == 0)
                            {
                                Directory.Delete(directory);
                                _logger.LogDebug("Removed empty directory: {Path}", directory);
                                removedAny = true;
                                continue;
                            }

                            // Directory with only metadata files - delete files then directory
                            if (files.All(f => ExpendableFileNames.Contains(Path.GetFileName(f))))
                            {
                                foreach (var file in files)
                                    File.Delete(file);

                                Directory.Delete(directory);
                                _logger.LogInformation(
                                    "Removed orphaned directory: {Path}", directory);
                                removedAny = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to remove directory: {Path}", directory);
                        }
                    }
                } while (removedAny);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during empty directory cleanup");
            }
        }).ConfigureAwait(false);
    }
}
