using System.Text.Json;

namespace RotmgTrackerV1.Core.Services;

/// <summary>Small persistent per-machine UI flags (ui_settings.json next to the database).</summary>
public class UiSettings
{
    private const string FileName = "ui_settings.json";

    private readonly object _sync = new();
    private readonly string _path;
    private bool _showLegacyOpenings;
    private bool _debugMode;

    public event Action? Changed;

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
            bool changed = false;
            lock (_sync)
            {
                if (_showLegacyOpenings != value)
                {
                    _showLegacyOpenings = value;
                    Save();
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }
    }

    /// <summary>Enables developer/debug actions (e.g. Test Boss DPS, manual fight archive, debug panel).</summary>
    public bool DebugMode
    {
        get { lock (_sync) return _debugMode; }
        set
        {
            bool changed = false;
            lock (_sync)
            {
                if (_debugMode != value)
                {
                    _debugMode = value;
                    Save();
                    changed = true;
                }
            }
            if (changed) Changed?.Invoke();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            var root = doc.RootElement;

            if (root.TryGetProperty("ShowLegacyOpenings", out var v) &&
                (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False))
            {
                _showLegacyOpenings = v.GetBoolean();
            }

            if (root.TryGetProperty("DebugMode", out var d) &&
                (d.ValueKind == JsonValueKind.True || d.ValueKind == JsonValueKind.False))
            {
                _debugMode = d.GetBoolean();
            }
        }
        catch
        {
            // Corrupted file: fall back to defaults.
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new
                {
                    ShowLegacyOpenings = _showLegacyOpenings,
                    DebugMode = _debugMode
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Not critical: flags just won't persist.
        }
    }
}
