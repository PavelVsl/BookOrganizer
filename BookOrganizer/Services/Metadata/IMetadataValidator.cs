using BookOrganizer.Models;

namespace BookOrganizer.Services.Metadata;

/// <summary>
/// Service for validating metadata quality and consistency.
/// </summary>
public interface IMetadataValidator
{
    /// <summary>
    /// Validates consolidated metadata and returns validation results.
    /// </summary>
    /// <param name="metadata">Metadata to validate.</param>
    /// <returns>Validation result with any issues found.</returns>
    ValidationResult Validate(ConsolidatedMetadata metadata);

    /// <summary>
    /// Validates a single metadata field value.
    /// </summary>
    /// <param name="fieldName">Name of the field being validated.</param>
    /// <param name="value">Value to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool ValidateField(string fieldName, object? value);
}
