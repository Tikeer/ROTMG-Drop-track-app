using System.Text.Json;

namespace RotmgTrackerV1.Core.Services;

/// <summary>
/// Ręczna tabela wartości itemów (item_value.json obok bazy). Mnożnik 1.0 = neutralny.
/// Klucz = nazwa itemu (bez prefiksu "Shiny "), wielkość liter bez znaczenia. Zmiany działają po restarcie aplikacji.
/// </summary>
public static class ItemValueTable
{
    private const string FileName = "item_value.json";
    private const double MinValue = 0.25;
    private const double MaxValue = 5.0;

    private static readonly object Sync = new();
    private static Dictionary<string, double>? _values;

    public static double GetMultiplier(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return 1.0;

        EnsureLoaded();

        string key = itemName.Trim();
        if (key.StartsWith("Shiny ", StringComparison.OrdinalIgnoreCase))
            key = key.Substring(6).Trim();

        return _values!.TryGetValue(key, out double value) ? value : 1.0;
    }

    private static void EnsureLoaded()
    {
        if (_values != null) return;

        lock (Sync)
        {
            if (_values != null) return;

            var loaded = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RotmgTracker");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, FileName);

                if (!File.Exists(path))
                {
                    File.WriteAllText(path,
                        "{\n  \"_info\": \"Mnoznik wartosci itemu (1.0 = neutralny, zakres 0.25-5.0). Klucz = nazwa itemu bez 'Shiny '. Zmiany dzialaja po restarcie aplikacji.\",\n  \"items\": {\n  }\n}\n");
                }
                else
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in items.EnumerateObject())
                        {
                            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDouble(out double v))
                                loaded[prop.Name.Trim()] = Math.Clamp(v, MinValue, MaxValue);
                        }
                    }
                }
            }
            catch
            {
                // Uszkodzony plik nie może blokować aplikacji - działamy na wartościach neutralnych
            }

            _values = loaded;
        }
    }
}
