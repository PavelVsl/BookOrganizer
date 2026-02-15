using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BookOrganizer.Services.Text;

/// <summary>
/// Loads a name dictionary from .bookorganizer/names.json and provides
/// diacritics-insensitive lookup to restore canonical Czech names.
/// </summary>
public class NameDictionary : INameDictionary
{
    private readonly ITextNormalizer _textNormalizer;
    private readonly ILogger<NameDictionary> _logger;

    // Keyed by NormalizeForComparison(name) → canonical name with diacritics
    private Dictionary<string, string>? _entries;

    public bool IsLoaded => _entries is { Count: > 0 };

    public NameDictionary(ITextNormalizer textNormalizer, ILogger<NameDictionary> logger)
    {
        _textNormalizer = textNormalizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LoadAsync(string libraryPath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(libraryPath, ".bookorganizer", "names.json");

        if (!File.Exists(filePath))
        {
            _logger.LogDebug("No name dictionary found at {Path}", filePath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (raw is null or { Count: 0 })
            {
                _logger.LogDebug("Name dictionary at {Path} is empty", filePath);
                return;
            }

            _entries = new Dictionary<string, string>(raw.Count, StringComparer.Ordinal);

            foreach (var (key, value) in raw)
            {
                var normalizedKey = _textNormalizer.NormalizeForComparison(key);
                _entries[normalizedKey] = value;
            }

            _logger.LogInformation("Loaded name dictionary with {Count} entries from {Path}", _entries.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load name dictionary from {Path}", filePath);
        }
    }

    /// <inheritdoc />
    public string Lookup(string name)
    {
        if (_entries is null || string.IsNullOrWhiteSpace(name))
            return name;

        var normalized = _textNormalizer.NormalizeForComparison(name);

        if (_entries.TryGetValue(normalized, out var canonical))
        {
            if (canonical != name)
            {
                _logger.LogDebug("Name dictionary: '{Original}' → '{Canonical}'", name, canonical);
            }
            return canonical;
        }

        return name;
    }
}
