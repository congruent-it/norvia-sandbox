using Microsoft.AspNetCore.Components;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>
/// A page that asks Norvia again every few seconds while it is open, at the interval its Persona has (P8). Each poll is a
/// real API Management call, so a Carrier in auto-progress visibly moves the screen along. The next wait starts when a poll
/// has finished, so a slow answer never piles polls up.
/// </summary>
public abstract class PollingPage : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _stopped = new();
    private Task? _polling;

    [Inject]
    protected TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>How long to wait between polls, read again each time so a page gets its Persona's interval once it has loaded.</summary>
    protected abstract TimeSpan PollInterval { get; }

    /// <summary>Asks Norvia again and updates what the page shows. It must not throw.</summary>
    protected abstract Task PollAsync(CancellationToken cancellationToken);

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            _polling = PollUntilStoppedAsync(_stopped.Token);
        }
    }

    private async Task PollUntilStoppedAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(PollInterval, Time, cancellationToken);
                await PollAsync(cancellationToken);
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
            // The page is gone.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopped.CancelAsync();
        if (_polling is not null)
        {
            await _polling;
        }

        _stopped.Dispose();
        GC.SuppressFinalize(this);
    }
}
