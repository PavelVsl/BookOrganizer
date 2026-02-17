using System;
using System.IO;
using System.Text.Json;

namespace BookOrganizer.Desktop.ViewModels;

/// <summary>
/// Persistent application settings stored as JSON.
/// </summary>
public class AppSettings
{
    public string? LibraryPath { get; set; }
    public string? SourcePath { get; set; }
    public string? DestinationPath { get; set; }

    public int ReorganizeModeIndex { get; set; } // 0=In-place, 1=Copy, 2=Move

    // Audiobookshelf settings
    public string? AbsServerUrl { get; set; }
    public string? AbsApiKey { get; set; }
    public string? AbsLibraryId { get; set; }
    public string? AbsLibraryName { get; set; }
    public string? AbsLibraryFolder { get; set; }

    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 700;
    public int SelectedNavIndex { get; set; }

    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BookOrganizer");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Ignore corrupted settings
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore write failures
        }
    }
}
