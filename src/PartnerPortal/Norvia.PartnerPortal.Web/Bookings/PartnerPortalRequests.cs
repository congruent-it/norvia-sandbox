using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Draft;
using Norvia.PartnerPortal.Application.Bookings.List;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.List;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.Personas.List;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// Sends the application's commands and queries, each in a scope of its own. A Blazor Server circuit lives as long as
/// the page is open, so a scope per circuit would keep one database context for all of it.
/// </summary>
internal sealed class PartnerPortalRequests(IServiceScopeFactory scopes) : IPartnerPortal
{
    public Task<IReadOnlyList<PersonaView>> ListPersonasAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListPersonasQuery(), cancellationToken);

    public async Task<BookingRequest?> DraftBookingAsync(string personaId, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new DraftBookingQuery(personaId), cancellationToken));

    public async Task<BookingView?> SubmitBookingAsync(string personaId, BookingRequest booking, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SubmitBookingCommand(personaId, booking), cancellationToken));
    }

    public Task<IReadOnlyList<BookingView>> ListBookingsAsync(string personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListBookingsQuery(personaId), cancellationToken);

    public Task<IReadOnlyList<CarrierBookingRequestView>> ListCarrierBookingRequestsAsync(string personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListCarrierBookingRequestsQuery(personaId), cancellationToken);

    private async Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(query, cancellationToken);
    }

    private static T? FoundOrNull<T>(Result<T> result)
        where T : class => result.Status switch
        {
            ResultStatus.Ok => result.Value,
            ResultStatus.NotFound => null,
            _ => throw new InvalidOperationException($"The request failed with {result.Status}."),
        };
}
