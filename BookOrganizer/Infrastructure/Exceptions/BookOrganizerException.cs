namespace BookOrganizer.Infrastructure.Exceptions;

/// <summary>
/// Base exception class for all BookOrganizer-specific exceptions.
/// </summary>
public class BookOrganizerException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BookOrganizerException"/> class.
    /// </summary>
    public BookOrganizerException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BookOrganizerException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public BookOrganizerException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BookOrganizerException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public BookOrganizerException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
