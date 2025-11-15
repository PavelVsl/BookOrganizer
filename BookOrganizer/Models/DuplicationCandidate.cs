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
    public required ConsolidatedMetadata SourceMetadata { get; init; }

    /// <summary>
    /// Target audiobook folder (the potential duplicate).
    /// </summary>
    public required AudiobookFolder TargetFolder { get; init; }

    /// <summary>
    /// Target audiobook metadata.
    /// </summary>
    public required ConsolidatedMetadata TargetMetadata { get; init; }

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
    Skip
}
