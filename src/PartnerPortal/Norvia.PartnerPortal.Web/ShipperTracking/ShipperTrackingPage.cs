using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.Bookings;
using Norvia.PartnerPortal.Web.Personas;

namespace Norvia.PartnerPortal.Web.ShipperTracking;

/// <summary>
/// What the Shipper tracking screens share: the Shipper Persona whose page it is (anyone else's is not found), the last
/// answer from Norvia, and a fetch that turns a failure into an error beside that answer instead of an exception.
/// </summary>
public abstract class ShipperTrackingPage<T> : PollingPage
    where T : class
{
    // One fetch at a time, each asking as the page is when it starts, so a slow poll never overwrites the answer to a newer question.
    private readonly SemaphoreSlim _fetching = new(1, 1);

    [Inject]
    protected IPartnerPortal Portal { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private ILogger<ShipperTrackingPage<T>> Logger { get; set; } = null!;

    [Parameter]
    public string PersonaId { get; set; } = string.Empty;

    /// <summary>The route's Persona, as the application knows it.</summary>
    protected PersonaId PersonaKey => new(PersonaId);

    protected LastAnswer<T> Answer { get; } = new();

    protected PersonaView? Persona { get; private set; }

    protected bool LoadFailed { get; private set; }

    protected override TimeSpan PollInterval => Persona?.TrackingPollInterval ?? ShipperProfile.DefaultTrackingPollInterval;

    /// <summary>Asks Norvia what the page shows, as the Persona.</summary>
    protected abstract Task<TrackingAnswer<T>> AskAsync(CancellationToken cancellationToken);

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            Persona = PersonaChoice.Find(PersonaId, await Portal.ListPersonasAsync(CancellationToken.None));
            if (Persona is null || Persona.PartyType != PartyType.Shipper)
            {
                Persona = null;
                Navigation.NotFound();
                return;
            }

            await FetchAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            PageLog.TrackingLoadFailed(Logger, exception, PersonaId);
            LoadFailed = true;
        }
    }

    protected override Task PollAsync(CancellationToken cancellationToken) => Persona is null ? Task.CompletedTask : FetchAsync(cancellationToken);

    // A fetch that fails keeps the last answer on screen and says why; the next poll tries again.
    protected async Task FetchAsync(CancellationToken cancellationToken)
    {
        await _fetching.WaitAsync(cancellationToken);
        try
        {
            Answer.Apply(await AskAsync(cancellationToken), Time.GetUtcNow());
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            PageLog.TrackingLoadFailed(Logger, exception, PersonaId);
            Answer.Fail("Tracking couldn't be fetched.");
        }
        finally
        {
            _fetching.Release();
        }
    }
}
