namespace BookOrganizer.Infrastructure.Exceptions;

/// <summary>
/// Exception thrown when file organization operations fail.
/// </summary>
public class FileOrganizationException : BookOrganizerException
{
    /// <summary>
    /// Gets the source path involved in the failed operation.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Gets the target path involved in the failed operation.
    /// </summary>
    public string? TargetPath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileOrganizationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="sourcePath">The source path involved in the failed operation.</param>
    /// <param name="targetPath">The target path involved in the failed operation.</param>
    public FileOrganizationException(string message, string? sourcePath = null, string? targetPath = null)
        : base(message)
    {
        SourcePath = sourcePath;
        TargetPath = targetPath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileOrganizationException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="sourcePath">The source path involved in the failed operation.</param>
    /// <param name="targetPath">The target path involved in the failed operation.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public FileOrganizationException(string message, string? sourcePath, string? targetPath, Exception innerException)
        : base(message, innerException)
    {
        SourcePath = sourcePath;
        TargetPath = targetPath;
    }
}
