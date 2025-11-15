namespace BookOrganizer.Models;

/// <summary>
/// Represents the result of metadata validation.
/// </summary>
public record ValidationResult
{
    /// <summary>
    /// Indicates whether the metadata is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// List of validation issues found (empty if valid).
    /// </summary>
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new ValidationResult { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with issues.
    /// </summary>
    public static ValidationResult Failure(params ValidationIssue[] issues) =>
        new ValidationResult
        {
            IsValid = false,
            Issues = issues
        };
}

/// <summary>
/// Represents a single validation issue.
/// </summary>
public record ValidationIssue
{
    /// <summary>
    /// Name of the field that failed validation.
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    /// Severity of the issue (Error, Warning, Info).
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// Description of the validation issue.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Current invalid value (for reference).
    /// </summary>
    public object? CurrentValue { get; init; }
}

/// <summary>
/// Severity levels for validation issues.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational message - does not prevent processing.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning - may indicate quality issues but processing can continue.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Error - serious issue that should be addressed.
    /// </summary>
    Error = 2
}
