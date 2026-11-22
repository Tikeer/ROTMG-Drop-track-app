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
    private bool _disposed;

    [Inject] protected TrackerStateService State { get; set; } = default!;

    protected override void OnInitialized()
    {
        State.OnStateChanged += HandleStateChanged;
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