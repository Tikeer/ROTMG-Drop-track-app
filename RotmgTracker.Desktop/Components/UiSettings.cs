using System.Text.Json;

namespace RotmgTrackerV1.Core.Services;

/// <summary>Small persistent UI flags (ui_settings.json next to the database).</summary>
public class UiSettings
{
    private const string FileName = "ui_settings.json";

    private readonly object _sync = new();
    private readonly string _path;
    private bool _showLegacyOpenings;

    public UiSettings()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RotmgTracker");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, FileName);
        Load();
    }

    /// <summary>Chest Openings (manual counters) are hidden by default; legacy feature.</summary>
    public bool ShowLegacyOpenings
    {
        get { lock (_sync) return _showLegacyOpenings; }
        set
        {
            lock (_sync)
            {
                if (_showLegacyOpenings == value) return;
                _showLegacyOpenings = value;
                Save();
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (doc.RootElement.TryGetProperty("ShowLegacyOpenings", out var v) &&
                (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
            {
                _showLegacyOpenings = v.GetBoolean();
            }
        }
        catch
        {
            // Corrupted file: fall back to defaults (everything hidden).
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new { ShowLegacyOpenings = _showLegacyOpenings },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Not critical: the flag just won't persist.
        }
    }
}