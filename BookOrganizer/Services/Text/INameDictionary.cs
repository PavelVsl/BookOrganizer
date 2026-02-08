namespace BookOrganizer.Services.Text;

/// <summary>
/// Dictionary for restoring proper diacritics in author/narrator names.
/// Loads from .bookorganizer/names.json in the library root.
/// </summary>
public interface INameDictionary
{
    /// <summary>
    /// Loads the name dictionary from .bookorganizer/names.json in the given library path.
    /// No-op if the file doesn't exist.
    /// </summary>
    /// <param name="libraryPath">Root path of the library</param>
    /// <param name="ct">Cancellation token</param>
    Task LoadAsync(string libraryPath, CancellationToken ct = default);

    /// <summary>
    /// Looks up the canonical name with proper diacritics.
    /// Returns the input unchanged if not found in the dictionary.
    /// </summary>
    /// <param name="name">Name to look up (may be ASCII-only)</param>
    /// <returns>Canonical name with diacritics, or original if not found</returns>
    string Lookup(string name);

    /// <summary>
    /// Whether the dictionary has been loaded and contains entries.
    /// </summary>
    bool IsLoaded { get; }
}
