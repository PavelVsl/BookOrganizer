using BookOrganizer.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Validates metadata quality and consistency.
/// </summary>
public partial class MetadataValidator : IMetadataValidator
{
    private readonly ILogger<MetadataValidator> _logger;

    // Validation constants
    private const int MinTitleLength = 1;
    private const int MaxTitleLength = 200;
    private const int MinAuthorLength = 2;
    private const int MaxAuthorLength = 100;
    private const int MinYearValue = 1900;
    private const double MinAcceptableConfidence = 0.1;

    public MetadataValidator(ILogger<MetadataValidator> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ValidationResult Validate(ConsolidatedMetadata metadata)
    {
        var issues = new List<ValidationIssue>();

        // Validate Title
        if (string.IsNullOrWhiteSpace(metadata.Title))
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.Title),
                Severity = ValidationSeverity.Error,
                Message = "Title is required",
                CurrentValue = metadata.Title
            });
        }
        else if (metadata.Title.Length < MinTitleLength || metadata.Title.Length > MaxTitleLength)
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.Title),
                Severity = ValidationSeverity.Warning,
                Message = $"Title length should be between {MinTitleLength} and {MaxTitleLength} characters",
                CurrentValue = metadata.Title
            });
        }
        else if (!IsValidText(metadata.Title))
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.Title),
                Severity = ValidationSeverity.Warning,
                Message = "Title contains invalid characters or formatting",
                CurrentValue = metadata.Title
            });
        }

        // Validate Title confidence
        if (metadata.TitleConfidence < MinAcceptableConfidence)
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.Title),
                Severity = ValidationSeverity.Warning,
                Message = $"Title confidence is very low ({metadata.TitleConfidence:F2})",
                CurrentValue = metadata.TitleConfidence
            });
        }

        // Validate Author
        if (!string.IsNullOrWhiteSpace(metadata.Author))
        {
            if (metadata.Author.Length < MinAuthorLength || metadata.Author.Length > MaxAuthorLength)
            {
                issues.Add(new ValidationIssue
                {
                    FieldName = nameof(metadata.Author),
                    Severity = ValidationSeverity.Warning,
                    Message = $"Author length should be between {MinAuthorLength} and {MaxAuthorLength} characters",
                    CurrentValue = metadata.Author
                });
            }
            else if (!IsValidText(metadata.Author))
            {
                issues.Add(new ValidationIssue
                {
                    FieldName = nameof(metadata.Author),
                    Severity = ValidationSeverity.Warning,
                    Message = "Author contains invalid characters or formatting",
                    CurrentValue = metadata.Author
                });
            }
        }
        else
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.Author),
                Severity = ValidationSeverity.Info,
                Message = "Author information is missing",
                CurrentValue = metadata.Author
            });
        }

        // Validate Year
        if (metadata.Year.HasValue)
        {
            var currentYear = DateTime.UtcNow.Year;
            if (metadata.Year.Value < MinYearValue || metadata.Year.Value > currentYear + 1)
            {
                issues.Add(new ValidationIssue
                {
                    FieldName = nameof(metadata.Year),
                    Severity = ValidationSeverity.Warning,
                    Message = $"Year should be between {MinYearValue} and {currentYear + 1}",
                    CurrentValue = metadata.Year
                });
            }
        }

        // Validate Series consistency
        if (!string.IsNullOrWhiteSpace(metadata.Series) && string.IsNullOrWhiteSpace(metadata.SeriesNumber))
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.SeriesNumber),
                Severity = ValidationSeverity.Info,
                Message = "Series is specified but series number is missing",
                CurrentValue = metadata.SeriesNumber
            });
        }

        if (string.IsNullOrWhiteSpace(metadata.Series) && !string.IsNullOrWhiteSpace(metadata.SeriesNumber))
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.Series),
                Severity = ValidationSeverity.Warning,
                Message = "Series number is specified but series name is missing",
                CurrentValue = metadata.Series
            });
        }

        // Validate Series Number format
        if (!string.IsNullOrWhiteSpace(metadata.SeriesNumber) && !IsValidSeriesNumber(metadata.SeriesNumber))
        {
            issues.Add(new ValidationIssue
            {
                FieldName = nameof(metadata.SeriesNumber),
                Severity = ValidationSeverity.Info,
                Message = "Series number format is unusual (expected numeric value like '1', '2.5', etc.)",
                CurrentValue = metadata.SeriesNumber
            });
        }

        // Validate overall confidence
        if (metadata.OverallConfidence < 0.3)
        {
            issues.Add(new ValidationIssue
            {
                FieldName = "OverallConfidence",
                Severity = ValidationSeverity.Warning,
                Message = $"Overall metadata confidence is low ({metadata.OverallConfidence:F2})",
                CurrentValue = metadata.OverallConfidence
            });
        }

        var result = issues.Any(i => i.Severity == ValidationSeverity.Error)
            ? ValidationResult.Failure(issues.ToArray())
            : new ValidationResult { IsValid = true, Issues = issues };

        if (!result.IsValid)
        {
            _logger.LogWarning(
                "Metadata validation failed with {ErrorCount} errors, {WarningCount} warnings",
                issues.Count(i => i.Severity == ValidationSeverity.Error),
                issues.Count(i => i.Severity == ValidationSeverity.Warning));
        }
        else if (issues.Any())
        {
            _logger.LogDebug(
                "Metadata validation passed with {IssueCount} warnings/info messages",
                issues.Count);
        }

        return result;
    }

    /// <inheritdoc />
    public bool ValidateField(string fieldName, object? value)
    {
        return fieldName switch
        {
            nameof(ConsolidatedMetadata.Title) =>
                value is string title && !string.IsNullOrWhiteSpace(title) &&
                title.Length >= MinTitleLength && title.Length <= MaxTitleLength &&
                IsValidText(title),

            nameof(ConsolidatedMetadata.Author) =>
                value is null ||
                (value is string author && (string.IsNullOrWhiteSpace(author) ||
                (author.Length >= MinAuthorLength && author.Length <= MaxAuthorLength &&
                IsValidText(author)))),

            nameof(ConsolidatedMetadata.Year) =>
                value is null ||
                (value is int year &&
                year >= MinYearValue && year <= DateTime.UtcNow.Year + 1),

            nameof(ConsolidatedMetadata.SeriesNumber) =>
                value is null ||
                (value is string seriesNum && (string.IsNullOrWhiteSpace(seriesNum) ||
                IsValidSeriesNumber(seriesNum))),

            _ => true // Unknown fields are considered valid
        };
    }

    private static bool IsValidText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for valid characters: letters (including Czech diacritics), numbers, spaces, common punctuation
        return ValidTextRegex().IsMatch(text);
    }

    private static bool IsValidSeriesNumber(string seriesNumber)
    {
        // Valid formats: "1", "2.5", "01", "3a", etc.
        return SeriesNumberRegex().IsMatch(seriesNumber);
    }

    // Regex patterns for validation
    [GeneratedRegex(@"^[\p{L}\p{N}\s\-.,!?'""():;/&]+$", RegexOptions.None, "cs-CZ")]
    private static partial Regex ValidTextRegex();

    [GeneratedRegex(@"^\d+(\.\d+)?[a-z]?$", RegexOptions.IgnoreCase)]
    private static partial Regex SeriesNumberRegex();
}
