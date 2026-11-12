using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Draft;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Application.Bookings.List;
using Norvia.PartnerPortal.Application.Bookings.Submit;
using Norvia.PartnerPortal.Application.CarrierBookings;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierBookings.List;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Application.CarrierInvoices.Draft;
using Norvia.PartnerPortal.Application.CarrierInvoices.List;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Application.CarrierShipments;
using Norvia.PartnerPortal.Application.CarrierShipments.List;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Application.DriverSignIns;
using Norvia.PartnerPortal.Application.DriverSignIns.List;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.Faults.List;
using Norvia.PartnerPortal.Application.Faults.Switch;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.Personas.List;
using Norvia.PartnerPortal.Application.ShipperInvoices;
using Norvia.PartnerPortal.Application.ShipperInvoices.List;
using Norvia.PartnerPortal.Application.ShipperNotifications;
using Norvia.PartnerPortal.Application.ShipperNotifications.List;
using Norvia.PartnerPortal.Application.ShipperTracking;
using Norvia.PartnerPortal.Application.ShipperTracking.DownloadFile;
using Norvia.PartnerPortal.Application.ShipperTracking.FindTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.GetTransportOrder;
using Norvia.PartnerPortal.Application.ShipperTracking.List;
using Norvia.PartnerPortal.Application.TrackingAssistant;
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
    public bool TrackingAssistantEnabled
    {
        get
        {
            using var scope = scopes.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IAssistantSwitch>().Enabled;
        }
    }

    public Task<AssistantAnswer> AskTrackingAssistantAsync(
        PersonaId personaId, string question, IReadOnlyList<AssistantTurn> history, string? openOrderNumber, CancellationToken cancellationToken) =>
        SendAsync(new AskTrackingAssistantCommand(personaId, question, history, openOrderNumber), cancellationToken);

    public Task<IReadOnlyList<FaultView>> ListFaultsAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListFaultsQuery(), cancellationToken);

    public Task<FaultView> SwitchFaultAsync(string owner, string name, TimeSpan? duration, CancellationToken cancellationToken) =>
        SendAsync(new SwitchFaultCommand(owner, name, duration), cancellationToken);

    public Task<DriverSignInsView> ListDriverSignInsAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListDriverSignInsQuery(), cancellationToken);

    public Task<IReadOnlyList<PersonaView>> ListPersonasAsync(CancellationToken cancellationToken) =>
        SendAsync(new ListPersonasQuery(), cancellationToken);

    public async Task<BookingRequest?> DraftBookingAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new DraftBookingQuery(personaId), cancellationToken));

    public async Task<BookingDraftAnswer?> DraftBookingFromTextAsync(PersonaId personaId, string text, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new DraftBookingFromTextQuery(personaId, text), cancellationToken));

    public async Task<BookingDraftAnswer?> DraftBookingFromPdfAsync(PersonaId personaId, byte[] pdf, CancellationToken cancellationToken) =>
        FoundOrNull(await SendAsync(new DraftBookingFromPdfQuery(personaId, pdf), cancellationToken));

    public async Task<BookingView?> SubmitBookingAsync(PersonaId personaId, BookingRequest booking, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return FoundOrNull(await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SubmitBookingCommand(personaId, booking), cancellationToken));
    }

    public Task<IReadOnlyList<BookingView>> ListBookingsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListBookingsQuery(personaId), cancellationToken);

    public Task<IReadOnlyList<CarrierBookingRequestView>> ListCarrierBookingRequestsAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListCarrierBookingRequestsQuery(personaId), cancellationToken);

    public Task<CarrierInvoiceDraftView?> GetCarrierInvoiceDraftAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new GetCarrierInvoiceDraftQuery(personaId), cancellationToken);

    public Task<Result<SubmittedCarrierInvoice>> SubmitCarrierInvoiceAsync(
        PersonaId personaId, string invoiceNumber, DateOnly invoiceDate, IReadOnlyList<CarrierInvoiceLineDraft> lines, CancellationToken cancellationToken) =>
        SendAsync(new SubmitCarrierInvoiceCommand(personaId, invoiceNumber, invoiceDate, lines), cancellationToken);

    public Task<IReadOnlyList<CarrierInvoiceView>> ListCarrierInvoicesAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListCarrierInvoicesQuery(personaId), cancellationToken);

    public Task<IReadOnlyList<ShipperInvoiceView>> ListShipperInvoicesAsync(PersonaId personaId, CancellationToken cancellationToken) =>
        SendAsync(new ListShipperInvoicesQuery(personaId), cancellationToken);

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

    private async Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(command, cancellationToken);
    }

    private static T? FoundOrNull<T>(Result<T> result)
        where T : class => result.Status switch
        {
            ResultStatus.Ok => result.Value,
            ResultStatus.NotFound => null,
            _ => throw new InvalidOperationException($"The request failed with {result.Status}."),
        };
}
