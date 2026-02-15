namespace BookOrganizer.Infrastructure.Exceptions;

/// <summary>
/// Exception thrown when metadata extraction fails.
/// </summary>
public class MetadataExtractionException : BookOrganizerException
{
    /// <summary>
    /// Gets the path of the file that caused the exception.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataExtractionException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="filePath">The path of the file that caused the exception.</param>
    public MetadataExtractionException(string message, string? filePath = null) : base(message)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataExtractionException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="filePath">The path of the file that caused the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public MetadataExtractionException(string message, string? filePath, Exception innerException)
        : base(message, innerException)
    {
        FilePath = filePath;
    }
}
