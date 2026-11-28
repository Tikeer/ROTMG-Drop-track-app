using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Core.Services.Sprites;
using RotmgTrackerV1.Sniffer.Services;

namespace RotmgTrackerV1.Desktop.Components;

/// <summary>
/// Asynchroniczny start aplikacji: dane gry i baza ładują się w tle (równolegle),
/// UI pokazuje ekran ładowania, a sniffer oraz atlas sprite'ów startują w tle po gotowości UI.
/// </summary>
public sealed class AppStartup
{
    private readonly GameDataService _gameData;
    private readonly TrackerStateService _state;
    private readonly RotmgSnifferService _sniffer;
    private readonly SpriteAtlasService _sprites;
    private readonly object _gate = new();
    private Task? _startTask;
    private volatile bool _isReady;

    public AppStartup(
        GameDataService gameData,
        TrackerStateService state,
        RotmgSnifferService sniffer,
        SpriteAtlasService sprites)
    {
        _gameData = gameData;
        _state = state;
        _sniffer = sniffer;
        _sprites = sprites;
    }

    public bool IsReady => _isReady;
    public string? Error { get; private set; }

    /// <summary>Wołane, gdy zmieni się stan startu (np. sniffer uruchomiony).</summary>
    public event Action? Changed;

    public Task StartAsync()
    {
        lock (_gate)
        {
            return _startTask ??= Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        var total = System.Diagnostics.Stopwatch.StartNew();
        long gameMs = 0, stateMs = 0;

        try
        {
            await Task.WhenAll(
                Task.Run(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _gameData.Initialize();
                    // Fast (~3 ms) cached atlas load so sprites are ready on the very first UI frame
                    _sprites.LoadCacheSync();
                    gameMs = sw.ElapsedMilliseconds;
                }),
                Task.Run(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _state.Initialize();
                    stateMs = sw.ElapsedMilliseconds;
                }));

            _isReady = true;
            LogTiming($"ready after {total.ElapsedMilliseconds} ms (game data {gameMs} ms, database {stateMs} ms, loot rows {_state.Data.LootHistory.Count})");
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LogTiming($"startup failed: {ex.Message}");
            Changed?.Invoke();
            return;
        }

        // Rebuild sprite atlas in background only if cache was missing or outdated
        _ = _sprites.EnsureBuiltAsync();

        _ = Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { _sniffer.StartListening(); }
            finally
            {
                LogTiming($"sniffer started in {sw.ElapsedMilliseconds} ms");
                Changed?.Invoke();
            }
        });
    }
    

    private static void LogTiming(string line)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RotmgTracker",
                "startup_timing.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}