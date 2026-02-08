namespace BookOrganizer.Models;

/// <summary>
/// Cached MP3 tag data for an entire audiobook folder.
/// Stored as mp3tags.json alongside the audio files.
/// </summary>
public record Mp3TagsCache
{
    /// <summary>
    /// Schema version for forward compatibility.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// UTC timestamp when this cache was created/updated.
    /// </summary>
    public required DateTime ScannedAtUtc { get; init; }

    /// <summary>
    /// Original folder path at the time of scanning (for tracing).
    /// </summary>
    public required string OriginalFolderPath { get; init; }

    /// <summary>
    /// Cached tag data for each audio file in the folder.
    /// </summary>
    public required List<CachedFileTag> Files { get; init; }
}

/// <summary>
/// Cached tag data for a single audio file.
/// </summary>
public record CachedFileTag
{
    /// <summary>
    /// Relative path within the audiobook folder.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// File modification time used for staleness detection.
    /// </summary>
    public required DateTime LastModifiedUtc { get; init; }

    /// <summary>
    /// File size used for staleness detection.
    /// </summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// Extracted tag data.
    /// </summary>
    public required CachedTagData Tags { get; init; }
}

/// <summary>
/// Raw ID3 tag values extracted from an audio file.
/// </summary>
public record CachedTagData
{
    public string? Title { get; init; }
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Composer { get; init; }
    public string? Genre { get; init; }
    public uint Year { get; init; }
    public string? Comment { get; init; }
    public double DurationSeconds { get; init; }
    public int Bitrate { get; init; }
}
