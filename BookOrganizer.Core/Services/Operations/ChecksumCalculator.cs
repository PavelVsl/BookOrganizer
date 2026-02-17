using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
namespace BookOrganizer.Services.Operations;

/// <summary>
/// Calculates file checksums for integrity validation.
/// </summary>
public class ChecksumCalculator
{
    private readonly ILogger<ChecksumCalculator> _logger;

    // Buffer size for streaming file reads (4MB)
    private const int BufferSize = 4 * 1024 * 1024;

    public ChecksumCalculator(ILogger<ChecksumCalculator> logger)
    {
        _logger = logger;
    }

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".m4b", ".flac", ".aac", ".ogg", ".opus", ".wma"
    };

    /// <summary>
    /// Calculates SHA256 hash of audio content only (excluding ID3/metadata tags).
    /// For non-audio files, falls back to full file SHA256 hash.
    /// </summary>
    public async Task<string> CalculateAudioContentHashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!System.IO.File.Exists(filePath))
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        var extension = Path.GetExtension(filePath);
        if (!AudioExtensions.Contains(extension))
        {
            return await CalculateChecksumAsync(filePath, ChecksumAlgorithm.SHA256, null, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            using var tagFile = TagLib.File.Create(filePath, TagLib.ReadStyle.None);
            var start = tagFile.InvariantStartPosition;
            var end = tagFile.InvariantEndPosition;

            _logger.LogDebug(
                "Hashing audio content for {FilePath}: bytes {Start}-{End}",
                filePath, start, end);

            using var sha256 = SHA256.Create();
            using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            fileStream.Seek(start, SeekOrigin.Begin);
            var remaining = end - start;
            var buffer = new byte[BufferSize];

            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var toRead = (int)Math.Min(buffer.Length, remaining);
                var read = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) break;

                sha256.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BytesToHexString(sha256.Hash ?? Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "TagLib failed for {FilePath}, falling back to full file hash", filePath);
            return await CalculateChecksumAsync(filePath, ChecksumAlgorithm.SHA256, null, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Calculates checksum for a file using the specified algorithm.
    /// Uses streaming to handle large files efficiently.
    /// </summary>
    public async Task<string> CalculateChecksumAsync(
        string filePath,
        ChecksumAlgorithm algorithm,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        var fileInfo = new FileInfo(filePath);
        var totalBytes = fileInfo.Length;

        _logger.LogDebug(
            "Calculating {Algorithm} checksum for file: {FilePath} ({Size} bytes)",
            algorithm,
            filePath,
            totalBytes);

        try
        {
            using var hashAlgorithm = CreateHashAlgorithm(algorithm);
            using var fileStream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[BufferSize];
            long bytesRead = 0;
            int currentRead;

            while ((currentRead = await fileStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hashAlgorithm.TransformBlock(buffer, 0, currentRead, null, 0);
                bytesRead += currentRead;
                progress?.Report(bytesRead);

                cancellationToken.ThrowIfCancellationRequested();
            }

            // Finalize hash calculation
            hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

            var hashBytes = hashAlgorithm.Hash ?? Array.Empty<byte>();
            var checksum = BytesToHexString(hashBytes);

            _logger.LogDebug(
                "Calculated {Algorithm} checksum for {FilePath}: {Checksum}",
                algorithm,
                filePath,
                checksum);

            return checksum;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Error calculating checksum for {FilePath}",
                filePath);
            throw;
        }
    }

    /// <summary>
    /// Validates that two files have matching checksums.
    /// </summary>
    public async Task<bool> ValidateIntegrityAsync(
        string sourceFilePath,
        string destinationFilePath,
        ChecksumAlgorithm algorithm,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Validating file integrity: {Source} -> {Destination}",
            sourceFilePath,
            destinationFilePath);

        var sourceChecksum = await CalculateChecksumAsync(
            sourceFilePath,
            algorithm,
            null,
            cancellationToken).ConfigureAwait(false);

        var destChecksum = await CalculateChecksumAsync(
            destinationFilePath,
            algorithm,
            null,
            cancellationToken).ConfigureAwait(false);

        var isValid = string.Equals(sourceChecksum, destChecksum, StringComparison.OrdinalIgnoreCase);

        if (isValid)
        {
            _logger.LogInformation(
                "File integrity validated successfully: {Checksum}",
                sourceChecksum);
        }
        else
        {
            _logger.LogWarning(
                "File integrity validation FAILED! Source: {SourceChecksum}, Destination: {DestChecksum}",
                sourceChecksum,
                destChecksum);
        }

        return isValid;
    }

    /// <summary>
    /// Creates the appropriate hash algorithm instance.
    /// </summary>
    private static HashAlgorithm CreateHashAlgorithm(ChecksumAlgorithm algorithm)
    {
        return algorithm switch
        {
            ChecksumAlgorithm.MD5 => MD5.Create(),
            ChecksumAlgorithm.SHA256 => SHA256.Create(),
            _ => throw new ArgumentException($"Unsupported checksum algorithm: {algorithm}", nameof(algorithm))
        };
    }

    /// <summary>
    /// Converts byte array to hexadecimal string.
    /// </summary>
    private static string BytesToHexString(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}
