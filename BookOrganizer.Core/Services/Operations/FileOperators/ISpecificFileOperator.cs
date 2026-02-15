using BookOrganizer.Models;

namespace BookOrganizer.Services.Operations.FileOperators;

/// <summary>
/// Interface for specific file operation implementations (Copy, Move, HardLink, SymbolicLink).
/// </summary>
public interface ISpecificFileOperator
{
    /// <summary>
    /// The type of file operation this operator performs.
    /// </summary>
    FileOperationType OperationType { get; }

    /// <summary>
    /// Executes the file operation.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Destination file path.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteAsync(
        string sourcePath,
        string destinationPath,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if this operator can execute the operation given the source and destination.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Destination file path.</param>
    /// <returns>True if the operation can be performed, false otherwise.</returns>
    bool CanExecute(string sourcePath, string destinationPath);

    /// <summary>
    /// Gets a human-readable description of what this operation does.
    /// </summary>
    string GetOperationDescription();
}
