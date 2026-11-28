using Microsoft.AspNetCore.Components;
using RotmgTrackerV1.Core.Services;

namespace RotmgTrackerV1.Desktop.Components;

/// <summary>
/// Base class for tab panels: subscribes to TrackerStateService.OnStateChanged and refreshes automatically.
/// Derived classes overriding OnInitialized must call base.OnInitialized().
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
