namespace BookOrganizer.Models;

/// <summary>
/// Represents a potential duplicate audiobook match.
/// </summary>
public record DuplicationCandidate
{
    /// <summary>
    /// Source audiobook folder (the original/existing one).
    /// </summary>
    public required AudiobookFolder SourceFolder { get; init; }

    /// <summary>
    /// Source audiobook metadata.
    /// </summary>
    public required BookMetadata SourceMetadata { get; init; }

    /// <summary>
    /// Target audiobook folder (the potential duplicate).
    /// </summary>
    public required AudiobookFolder TargetFolder { get; init; }

    /// <summary>
    /// Target audiobook metadata.
    /// </summary>
    public required BookMetadata TargetMetadata { get; init; }

    /// <summary>
    /// Confidence score that these are duplicates (0.0 = unlikely, 1.0 = very likely).
    /// </summary>
    public required double ConfidenceScore { get; init; }

    /// <summary>
    /// Reasons why these are considered duplicates.
    /// </summary>
    public required List<string> MatchReasons { get; init; }

    /// <summary>
    /// Detected differences between the two versions.
    /// </summary>
    public required List<string> Differences { get; init; }

    /// <summary>
    /// Recommended resolution action.
    /// </summary>
    public DuplicationResolution RecommendedResolution { get; init; }

    /// <summary>
    /// Scope of the duplicate detection (within source or against existing library).
    /// </summary>
    public DuplicationScope Scope { get; init; } = DuplicationScope.WithinSource;

    /// <summary>
    /// Whether this duplicate should be automatically merged during organization.
    /// True if the recommended resolution is not KeepBoth and confidence is high enough.
    /// </summary>
    public bool MergeAutomatically =>
        RecommendedResolution != DuplicationResolution.KeepBoth &&
        RecommendedResolution != DuplicationResolution.Skip &&
        ConfidenceScore >= 0.8;
}

/// <summary>
/// Scope of duplicate detection.
/// </summary>
public enum DuplicationScope
{
    /// <summary>
    /// Duplicate found within source audiobooks being organized.
    /// </summary>
    WithinSource,

    /// <summary>
    /// Duplicate found against existing library books.
    /// </summary>
    WithExistingLibrary
}

/// <summary>
/// Resolution options for handling duplicates.
/// </summary>
public enum DuplicationResolution
{
    /// <summary>
    /// Not yet decided.
    /// </summary>
    Undecided,

    /// <summary>
    /// Keep both versions with version suffixes.
    /// </summary>
    KeepBoth,

    /// <summary>
    /// Keep the source (existing) version.
    /// </summary>
    KeepSource,

    /// <summary>
    /// Keep the target (new) version.
    /// </summary>
    KeepTarget,

    /// <summary>
    /// Skip/ignore during organization.
    /// </summary>
    Skip,

    /// <summary>
    /// Merge both versions into a single folder (consolidate all files).
    /// </summary>
    Merge
}
