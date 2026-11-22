using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using RotmgTrackerV1.Core.Services;

namespace RotmgTrackerV1.Desktop.Components;

/// <summary>
/// Baza dla paneli zakładek: subskrybuje TrackerStateService.OnStateChanged i odświeża się sama.
/// Potrzebna, bo Blazor nie renderuje ponownie komponentu bez parametrów, gdy renderuje się jego rodzic.
/// Klasa pochodna, która nadpisuje OnInitialized, musi wywołać base.OnInitialized().
/// </summary>
public abstract class StateAwareComponent : ComponentBase, IDisposable
{
    private readonly Stopwatch _created = Stopwatch.StartNew();
    private bool _disposed;

    [Inject] protected TrackerStateService State { get; set; } = default!;

    protected override void OnInitialized()
    {
        State.OnStateChanged += HandleStateChanged;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            UiTiming.Log($"{GetType().Name} first render {_created.ElapsedMilliseconds} ms");
    }

    private void HandleStateChanged()
    {
        if (_disposed) return;

        _ = InvokeAsync(() =>
        {
            if (!_disposed)
                StateHasChanged();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        State.OnStateChanged -= HandleStateChanged;
    }
}

/// <summary>Tymczasowy log czasów renderu (ui_timing.log obok bazy). Do usunięcia po diagnostyce.</summary>
internal static class UiTiming
{
    public static void Log(string line)
    {
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RotmgTracker",
                "ui_timing.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch { }
    }
}