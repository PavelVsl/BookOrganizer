using System.Diagnostics;
using BookOrganizer.Models;
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

    public FileOrganizer(
        ILogger<FileOrganizer> logger,
        IDirectoryScanner directoryScanner,
        IMetadataExtractor metadataExtractor,
        IPathGenerator pathGenerator,
        IFileOperator fileOperator)
    {
        _logger = logger;
        _directoryScanner = directoryScanner;
        _metadataExtractor = metadataExtractor;
        _pathGenerator = pathGenerator;
        _fileOperator = fileOperator;
    }

    public async Task<OrganizationResult> OrganizeAsync(
        string sourcePath,
        string destinationPath,
        FileOperationType operationType,
        bool validateIntegrity = true,
        IProgress<OrganizationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting organization: {Source} -> {Destination} ({OperationType})",
            sourcePath,
            destinationPath,
            operationType);

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
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var audiobook in audiobooks)
            {
                // Extract metadata
                var metadata = await _metadataExtractor.ExtractMetadataAsync(audiobook, cancellationToken)
                    .ConfigureAwait(false);

                // Generate target path
                var targetPath = _pathGenerator.GenerateTargetPath(metadata, destinationPath);

                // Ensure unique path
                if (existingPaths.Contains(targetPath))
                {
                    targetPath = _pathGenerator.EnsureUniquePath(metadata, targetPath, existingPaths);
                }
                existingPaths.Add(targetPath);

                plans.Add(new OrganizationPlan
                {
                    SourceFolder = audiobook,
                    Metadata = metadata,
                    TargetPath = targetPath,
                    OperationType = operationType
                });
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

        // Process all files (audio + cover images/metadata)
        foreach (var sourceFile in plan.SourceFolder.AllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fileName = Path.GetFileName(sourceFile);
                var destinationFile = Path.Combine(plan.TargetPath, fileName);

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
}
