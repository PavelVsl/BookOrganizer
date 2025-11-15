using BookOrganizer.Models;

namespace BookOrganizer.Services.Operations;

/// <summary>
/// Service for organizing audiobook files according to a plan.
/// </summary>
public interface IFileOrganizer
{
    /// <summary>
    /// Creates an organization plan for an audiobook.
    /// </summary>
    /// <param name="audiobookFolder">The audiobook folder to organize.</param>
    /// <param name="metadata">Consolidated metadata for the audiobook.</param>
    /// <param name="destinationRoot">Root destination directory.</param>
    /// <param name="operationType">Type of operation (copy or move).</param>
    /// <returns>Organization plan with target paths.</returns>
    OrganizationPlan CreatePlan(
        AudiobookFolder audiobookFolder,
        BookMetadata metadata,
        string destinationRoot,
        FileOperationType operationType);

    /// <summary>
    /// Executes an organization plan, copying or moving files.
    /// </summary>
    /// <param name="plan">The organization plan to execute.</param>
    /// <param name="dryRun">If true, simulates the operation without making changes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> ExecutePlanAsync(
        OrganizationPlan plan,
        bool dryRun = false,
        CancellationToken cancellationToken = default);
}
