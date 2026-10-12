using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Draft;
using Norvia.PartnerPortal.Application.Bookings.List;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierBookings.List;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.List;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Application.DriverAppFault;
using Norvia.PartnerPortal.Application.DriverAppFault.GetCurrent;
using Norvia.PartnerPortal.Application.DriverAppFault.Switch;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.Personas.List;
using Norvia.PartnerPortal.Application.ShipperNotifications;
using Norvia.PartnerPortal.Application.ShipperNotifications.List;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;
using Norvia.PartnerPortal.Application.ShipperTracking.FindTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.GetTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.List;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Web.CarrierShipments;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// Sends the application's commands and queries, each in a scope of its own. A Blazor Server circuit lives as long as
/// the page is open, so a scope per circuit would keep one database context for all of it.
/// </summary>
internal sealed class PartnerPortalRequests(IServiceScopeFactory scopes) : IPartnerPortal
{
    public Task<DriverAppFaultView> GetDriverAppFaultAsync(CancellationToken cancellationToken) =>
        SendAsync(new GetDriverAppFaultQuery(), cancellationToken);

    public async Task<DriverAppFaultView> SetDriverAppFaultAsync(bool unavailable, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SetDriverAppFaultCommand(unavailable), cancellationToken);
    }

    public Task<IReadOnlyList<PersonaView>> ListPersonasAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListPersonasQuery(), cancellationToken);

    public async Task<BookingRequest?> DraftBookingAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new DraftBookingQuery(personaId), cancellationToken));

    public async Task<BookingView?> SubmitBookingAsync(PersonaId personaId, BookingRequest booking, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SubmitBookingCommand(personaId, booking), cancellationToken));
    }

    public Task<IReadOnlyList<BookingView>> ListBookingsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListBookingsQuery(personaId), cancellationToken);

    public Task<IReadOnlyList<CarrierBookingRequestView>> ListCarrierBookingRequestsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListCarrierBookingRequestsQuery(personaId), cancellationToken);

    public Task<IReadOnlyList<ShipperNotificationView>> ListShipperNotificationsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListShipperNotificationsQuery(personaId), cancellationToken);

    public async Task<AnsweredCarrierBooking?> AnswerCarrierBookingAsync(
        PersonaId personaId, string carrierBookingNumber, CarrierAnswer answer, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new AnswerCarrierBookingCommand(personaId, carrierBookingNumber, answer), cancellationToken));
    }

    public async Task<CarrierAnswerSettingsView?> GetCarrierAnswerSettingsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new GetCarrierAnswerSettingsQuery(personaId), cancellationToken));

    public async Task<CarrierAnswerSettingsView?> ChangeCarrierAnswerSettingsAsync(
        PersonaId personaId, CarrierAnswerSettingsView settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ChangeCarrierAnswerSettingsCommand(personaId, settings.Mode, settings.RejectionReason, settings.DelaySeconds), cancellationToken));
    }

    public Task<IReadOnlyList<CarrierShipmentView>> ListCarrierShipmentsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListCarrierShipmentsQuery(personaId), cancellationToken);

    public async Task<SentTrackingEvent?> SendTrackingEventAsync(
        PersonaId personaId, string carrierBookingNumber, TrackingEventType type, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new SendTrackingCommand(personaId, carrierBookingNumber, type), cancellationToken));
    }

    public async Task<SentProofOfDelivery?> SendProofOfDeliveryAsync(
        PersonaId personaId, string carrierBookingNumber, ProofOfDeliveryDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new SendProofOfDeliveryCommand(personaId, carrierBookingNumber, draft.ReceivedBy, draft.Condition, draft.Remark, draft.Files), cancellationToken));
    }

    public async Task<CarrierTrackingSettingsView?> GetCarrierTrackingSettingsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new GetCarrierTrackingSettingsQuery(personaId), cancellationToken));

    public async Task<CarrierTrackingSettingsView?> ChangeCarrierTrackingSettingsAsync(
        PersonaId personaId, CarrierTrackingSettingsView settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new ChangeCarrierTrackingSettingsCommand(personaId, settings.Mode, settings.DelaySeconds), cancellationToken));
    }

    public Task<TrackingAnswer<IReadOnlyList<ShipperTrackingSummary>>> ListTrackedTransportOrdersAsync(
        PersonaId personaId, ShipperTrackingFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return SendAsync(new ListShipperTrackingQuery(personaId, filter), cancellationToken);
    }

    public Task<TrackingAnswer<ShipperTrackingDetail>> GetTrackedTransportOrderAsync(PersonaId personaId, string transportOrderNumber, CancellationToken cancellationToken) =>
        SendAsync(new GetShipperTrackingQuery(personaId, transportOrderNumber), cancellationToken);

    public Task<TrackingAnswer<ShipperTrackingDetail>> FindTrackedTransportOrderAsync(PersonaId personaId, string shipperReference, CancellationToken cancellationToken) =>
        SendAsync(new FindShipperTrackingQuery(personaId, shipperReference), cancellationToken);

    public Task<TrackingAnswer<ShipperProofFileContent>> DownloadProofOfDeliveryFileAsync(
        PersonaId personaId, string transportOrderNumber, Guid fileId, CancellationToken cancellationToken) =>
        SendAsync(new DownloadProofOfDeliveryFileQuery(personaId, transportOrderNumber, fileId), cancellationToken);

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
