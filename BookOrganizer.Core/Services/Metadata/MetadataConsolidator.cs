using BookOrganizer.Models;
using BookOrganizer.Services.Text;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Consolidates metadata from multiple sources with confidence scoring.
/// </summary>
public class MetadataConsolidator : IMetadataConsolidator
{
    private readonly ILogger<MetadataConsolidator> _logger;
    private readonly ITextNormalizer _textNormalizer;
    private readonly INameDictionary _nameDictionary;

    // Source reliability weights (higher = more reliable)
    // Hierarchical metadata.json files have HIGHEST priority (explicit user overrides)
    private const double HierarchicalMetadataJsonWeight = 2.0;
    private const double Id3TagsWeight = 1.0;
    private const double FilenameWeight = 0.6;
    private const double FolderStructureWeight = 0.4;

    public MetadataConsolidator(ILogger<MetadataConsolidator> logger, ITextNormalizer textNormalizer, INameDictionary nameDictionary)
    {
        _logger = logger;
        _textNormalizer = textNormalizer;
        _nameDictionary = nameDictionary;
    }

    /// <inheritdoc />
    public Task<ConsolidatedMetadata> ConsolidateAsync(
        IEnumerable<BookMetadata> metadataSources,
        CancellationToken cancellationToken = default)
    {
        var sourcesList = metadataSources.ToList();

        if (sourcesList.Count == 0)
        {
            throw new ArgumentException("At least one metadata source is required", nameof(metadataSources));
        }

        _logger.LogDebug(
            "Consolidating metadata from {Count} sources: {Sources}",
            sourcesList.Count,
            string.Join(", ", sourcesList.Select(s => s.Source)));

        // If only one source, convert directly
        if (sourcesList.Count == 1)
        {
            return Task.FromResult(ConsolidatedMetadata.FromBookMetadata(sourcesList[0]));
        }

        // Consolidate each field independently
        var consolidated = new ConsolidatedMetadata
        {
            Title = ConsolidateField(
                sourcesList,
                m => m.Title,
                out var titleConf,
                out var titleSource),
            TitleConfidence = titleConf,
            TitleSource = titleSource,

            Author = ConsolidateField(
                sourcesList,
                m => m.Author,
                out var authorConf,
                out var authorSource,
                "Author"),
            AuthorConfidence = authorConf,
            AuthorSource = authorSource,

            Series = ConsolidateField(
                sourcesList,
                m => m.Series,
                out var seriesConf,
                out var seriesSource),
            SeriesConfidence = seriesConf,
            SeriesSource = seriesSource,

            SeriesNumber = ConsolidateField(
                sourcesList,
                m => m.SeriesNumber,
                out var seriesNumConf,
                out var seriesNumSource),
            SeriesNumberConfidence = seriesNumConf,
            SeriesNumberSource = seriesNumSource,

            Narrator = ConsolidateField(
                sourcesList,
                m => m.Narrator,
                out var narratorConf,
                out var narratorSource,
                "Narrator"),
            NarratorConfidence = narratorConf,
            NarratorSource = narratorSource,

            Year = ConsolidateYearField(
                sourcesList,
                out var yearConf,
                out var yearSource),
            YearConfidence = yearConf,
            YearSource = yearSource,

            Genre = ConsolidateField(
                sourcesList,
                m => m.Genre,
                out var genreConf,
                out var genreSource),
            GenreConfidence = genreConf,
            GenreSource = genreSource,

            Description = ConsolidateField(
                sourcesList,
                m => m.Description,
                out var descConf,
                out var descSource),
            DescriptionConfidence = descConf,
            DescriptionSource = descSource,

            Language = ConsolidateField(
                sourcesList,
                m => m.Language,
                out _,
                out _),

            DiscNumber = ConsolidateDiscNumberField(sourcesList),

            ContributingSources = sourcesList.Select(s => s.Source).Distinct().ToList()
        };

        // Calculate overall confidence as weighted average of field confidences
        var overallConfidence = CalculateOverallConfidence(consolidated);

        consolidated = consolidated with { OverallConfidence = overallConfidence };

        _logger.LogInformation(
            "Consolidated metadata: Title='{Title}' (conf={TitleConf:F2}), Author='{Author}' (conf={AuthorConf:F2}), Overall={Overall:F2}",
            consolidated.Title,
            consolidated.TitleConfidence,
            consolidated.Author,
            consolidated.AuthorConfidence,
            consolidated.OverallConfidence);

        return Task.FromResult(consolidated);
    }

    private string ConsolidateField(
        List<BookMetadata> sources,
        Func<BookMetadata, string?> fieldSelector,
        out double confidence,
        out string? source,
        string? fieldName = null)
    {
        // Get all non-null values with their sources and base confidence
        var candidates = sources
            .Select(s => new
            {
                Value = fieldSelector(s),
                Source = s.Source,
                BaseConfidence = s.Confidence,
                Weight = GetSourceWeight(s.Source)
            })
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToList();

        // If no valid values, return default
        if (candidates.Count == 0)
        {
            confidence = 0.0;
            source = null;
            return string.Empty;
        }

        // If only one value, use it
        if (candidates.Count == 1)
        {
            var single = candidates[0];
            confidence = single.BaseConfidence * single.Weight;
            source = single.Source;
            var singleResult = _textNormalizer.NormalizeForDisplay(single.Value!);
            if (fieldName is "Author" or "Narrator" && _nameDictionary.IsLoaded)
                singleResult = _nameDictionary.Lookup(singleResult);
            return singleResult;
        }

        // Multiple values - prefer higher weighted sources with higher base confidence
        var best = candidates
            .OrderByDescending(c => c.Weight * c.BaseConfidence)
            .ThenByDescending(c => c.Value!.Length) // Prefer more complete information
            .First();

        // Calculate confidence based on agreement between sources
        // Use text normalization for Czech-aware comparison
        var agreementCount = candidates.Count(c =>
            _textNormalizer.AreEquivalent(c.Value, best.Value));

        var agreementBonus = agreementCount > 1 ? 0.1 * (agreementCount - 1) : 0.0;

        confidence = Math.Min(1.0, (best.BaseConfidence * best.Weight) + agreementBonus);
        source = best.Source;

        // Normalize the best value for display (fix encoding but preserve case/diacritics)
        var result = _textNormalizer.NormalizeForDisplay(best.Value!);

        // Apply name dictionary lookup for Author and Narrator fields
        if (fieldName is "Author" or "Narrator" && _nameDictionary.IsLoaded)
            result = _nameDictionary.Lookup(result);

        return result;
    }

    private int? ConsolidateYearField(
        List<BookMetadata> sources,
        out double confidence,
        out string? source)
    {
        // Get all valid years with their sources
        var candidates = sources
            .Select(s => new
            {
                Value = s.Year,
                Source = s.Source,
                BaseConfidence = s.Confidence,
                Weight = GetSourceWeight(s.Source)
            })
            .Where(c => c.Value.HasValue && IsValidYear(c.Value.Value))
            .ToList();

        // If no valid values, return null
        if (candidates.Count == 0)
        {
            confidence = 0.0;
            source = null;
            return null;
        }

        // Prefer higher weighted sources with higher base confidence
        var best = candidates
            .OrderByDescending(c => c.Weight * c.BaseConfidence)
            .First();

        // Calculate confidence based on agreement
        var agreementCount = candidates.Count(c => c.Value == best.Value);
        var agreementBonus = agreementCount > 1 ? 0.1 * (agreementCount - 1) : 0.0;

        confidence = Math.Min(1.0, (best.BaseConfidence * best.Weight) + agreementBonus);
        source = best.Source;

        return best.Value;
    }

    private static int? ConsolidateDiscNumberField(List<BookMetadata> sources)
    {
        // Take the first non-null DiscNumber from the highest-weighted source
        return sources
            .Where(s => s.DiscNumber.HasValue)
            .OrderByDescending(s => GetSourceWeight(s.Source) * s.Confidence)
            .Select(s => s.DiscNumber)
            .FirstOrDefault();
    }

    private static bool IsValidYear(int year)
    {
        // Reasonable range for audiobooks
        return year >= 1900 && year <= DateTime.UtcNow.Year + 1;
    }

    private static double GetSourceWeight(string source)
    {
        return source switch
        {
            "HierarchicalMetadataJson" => HierarchicalMetadataJsonWeight,
            "ID3Tags" => Id3TagsWeight,
            "FilenameParser" => FilenameWeight,
            _ when source.Contains("Folder", StringComparison.OrdinalIgnoreCase) => FolderStructureWeight,
            _ => 0.5 // Unknown source gets medium weight
        };
    }

    private static double CalculateOverallConfidence(ConsolidatedMetadata metadata)
    {
        // Field weights based on importance for audiobook organization
        var weights = new Dictionary<string, (double confidence, double weight)>
        {
            { "Title", (metadata.TitleConfidence, 0.30) },
            { "Author", (metadata.AuthorConfidence, 0.25) },
            { "Series", (metadata.SeriesConfidence, 0.15) },
            { "SeriesNumber", (metadata.SeriesNumberConfidence, 0.10) },
            { "Narrator", (metadata.NarratorConfidence, 0.10) },
            { "Year", (metadata.YearConfidence, 0.05) },
            { "Genre", (metadata.GenreConfidence, 0.03) },
            { "Description", (metadata.DescriptionConfidence, 0.02) }
        };

        // Calculate weighted average
        var totalWeight = 0.0;
        var weightedSum = 0.0;

        foreach (var (_, (conf, weight)) in weights)
        {
            if (conf > 0) // Only include fields that have values
            {
                weightedSum += conf * weight;
                totalWeight += weight;
            }
        }

        return totalWeight > 0 ? weightedSum / totalWeight : 0.0;
    }
}
