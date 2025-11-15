namespace BookOrganizer.Infrastructure.Exceptions;

/// <summary>
/// Exception thrown when directory scanning fails.
/// </summary>
public class DirectoryScanningException : BookOrganizerException
{
    /// <summary>
    /// Gets the directory path that caused the exception.
    /// </summary>
    public string? DirectoryPath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryScanningException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="directoryPath">The directory path that caused the exception.</param>
    public DirectoryScanningException(string message, string? directoryPath = null) : base(message)
    {
        DirectoryPath = directoryPath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectoryScanningException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="directoryPath">The directory path that caused the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public DirectoryScanningException(string message, string? directoryPath, Exception innerException)
        : base(message, innerException)
    {
        DirectoryPath = directoryPath;
    }
}
