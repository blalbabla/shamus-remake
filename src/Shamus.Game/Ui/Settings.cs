using System.Text.Json;
using Shamus.Core;

namespace Shamus.Game.Ui;

/// <summary>Persistent user settings (JSON in %APPDATA%\Shamus3D).</summary>
public sealed class Settings
{
    public string Maze { get; set; } = "c64";
    public Difficulty Difficulty { get; set; } = Difficulty.Advanced;
    public float Volume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.6f;
    public bool Muted { get; set; }
    public bool GodMode { get; set; }
    public bool Fullscreen { get; set; }
    public int Seed { get; set; } = 1;
    public Shamus.Game.Rendering.ViewMode View { get; set; } = Shamus.Game.Rendering.ViewMode.Top;
    public Dictionary<string, int> HighScores { get; set; } = new();

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shamus3D", "settings.json");

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { /* corrupt file: fall back to defaults */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read-only profile etc.: ignore */ }
    }

    public int HighScore(string maze) => HighScores.TryGetValue(maze, out var v) ? v : 0;

    /// <summary>Records a score; returns true if it is a new high score.</summary>
    public bool Submit(string maze, int score)
    {
        if (score <= HighScore(maze)) return false;
        HighScores[maze] = score; Save(); return true;
    }
}
