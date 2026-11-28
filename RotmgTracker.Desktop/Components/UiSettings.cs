using System.Text.Json;

namespace RotmgTrackerV1.Core.Services;

/// <summary>Small persistent per-machine UI flags (ui_settings.json next to the database).</summary>
public class UiSettings
{
    private const string FileName = "ui_settings.json";
    public static readonly string[] DefaultCharacterBarOrder = ["fame", "exalt", "stars", "gold"];

    private readonly object _sync = new();
    private readonly string _path;
    private bool _showLegacyOpenings;
    private bool _debugMode;
    private List<string> _characterBarOrder = new(DefaultCharacterBarOrder);

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

    public IReadOnlyList<string> CharacterBarOrder
    {
        get { lock (_sync) return _characterBarOrder.ToList(); }
    }

    public void SetCharacterBarOrder(IEnumerable<string> order)
    {
        var normalized = NormalizeOrder(order);
        bool changed = false;
        lock (_sync)
        {
            if (!_characterBarOrder.SequenceEqual(normalized))
            {
                _characterBarOrder = normalized;
                Save();
                changed = true;
            }
        }
        if (changed) Changed?.Invoke();
    }

    private static List<string> NormalizeOrder(IEnumerable<string>? raw)
    {
        var result = new List<string>();
        if (raw != null)
        {
            foreach (var item in raw)
            {
                if (DefaultCharacterBarOrder.Contains(item) && !result.Contains(item))
                    result.Add(item);
            }
        }
        foreach (var def in DefaultCharacterBarOrder)
        {
            if (!result.Contains(def))
                result.Add(def);
        }
        return result;
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
                _showLegacyOpenings = v.GetBoolean();

            if (root.TryGetProperty("DebugMode", out var d) &&
                (d.ValueKind == JsonValueKind.True || d.ValueKind == JsonValueKind.False))
                _debugMode = d.GetBoolean();

            if (root.TryGetProperty("CharacterBarOrder", out var ord) && ord.ValueKind == JsonValueKind.Array)
            {
                var list = ord.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString()!)
                    .ToList();
                _characterBarOrder = NormalizeOrder(list);
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new
                {
                    ShowLegacyOpenings = _showLegacyOpenings,
                    DebugMode = _debugMode,
                    CharacterBarOrder = _characterBarOrder
                },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch { }
    }
}
