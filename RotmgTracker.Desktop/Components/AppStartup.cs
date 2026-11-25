using RotmgTrackerV1.Core.Services;
using RotmgTrackerV1.Sniffer.Services;

namespace RotmgTrackerV1.Desktop.Components;

/// <summary>
/// Asynchroniczny start aplikacji: dane gry i baza ładują się w tle (równolegle),
/// UI pokazuje ekran ładowania, a sniffer startuje dopiero po gotowości UI.
/// </summary>
public sealed class AppStartup
{
    private readonly GameDataService _gameData;
    private readonly TrackerStateService _state;
    private readonly RotmgSnifferService _sniffer;
    private readonly object _gate = new();
    private Task? _startTask;
    private volatile bool _isReady;

    public AppStartup(GameDataService gameData, TrackerStateService state, RotmgSnifferService sniffer)
    {
        _gameData = gameData;
        _state = state;
        _sniffer = sniffer;
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

        StartPoolProbe();

        try
        {
            await Task.WhenAll(
                Task.Run(() =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _gameData.Initialize();
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
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            LogTiming($"startup failed: {ex.Message}");
            return;
        }

        if (Environment.GetEnvironmentVariable("ROTMG_NO_SNIFFER") == "1")
        {
            LogTiming("sniffer DISABLED by ROTMG_NO_SNIFFER");
            Changed?.Invoke();
            return;
        }

        // Sniffer (otwieranie interfejsów pcap) w tle, bez blokowania UI
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
    
    // Diagnostyka: co 500 ms wrzuca zadanie do puli wątków i mierzy, ile czeka na uruchomienie
    private static void StartPoolProbe()
    {
        var thread = new Thread(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 20000)
                {
                    long queuedAt = sw.ElapsedMilliseconds;
                    long ranAt = -1;
                    using var done = new ManualResetEventSlim(false);
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        ranAt = sw.ElapsedMilliseconds;
                        done.Set();
                    });
                    bool ok = done.Wait(5000);
                    string delay = ok ? $"{ranAt - queuedAt} ms" : ">5000 ms";
                    LogTiming($"pool probe t={queuedAt} ms: queue delay {delay}, threads {ThreadPool.ThreadCount}, pending {ThreadPool.PendingWorkItemCount}");
                    Thread.Sleep(500);
                }
            })
            { IsBackground = true };
        thread.Start();
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