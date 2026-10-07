using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.Send;

public sealed partial class SendTrackingHandler(
    IRepository<CarrierBookingRequest> repository,
    INorviaPartnerApi partnerApi,
    IPersonaDirectory personas,
    TimeProvider time,
    ILogger<SendTrackingHandler> logger)
    : ICommandHandler<SendTrackingCommand, Result<SentTrackingEvent>>
{
    public async ValueTask<Result<SentTrackingEvent>> Handle(SendTrackingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(command.PersonaId) is not { PartyType: PartyType.Carrier } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{command.PersonaId}'.");
        }

        var request = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(carrier.Id, command.CarrierBookingNumber), cancellationToken);
        if (request is not { Status: CarrierBookingRequestStatus.Confirmed, CarrierBookingReference: { } reference })
        {
            return Result.NotFound($"{carrier.DisplayName} has no Shipment for booking {command.CarrierBookingNumber}.");
        }

        if (!TrackingRules.IsOffered(request.LastProgress, command.Type))
        {
            return Result.Invalid(new ValidationError($"{command.Type} isn't a valid next Tracking event for a {request.Stage} Shipment."));
        }

        if (LocationOf(request.Details, command.Type) is not { City: { Length: > 0 } place, Postcode: { Length: > 0 } postcode, Country: { Length: > 0 } country })
        {
            return Result.Invalid(new ValidationError($"The Shipment's address has no place, postcode and country to report {command.Type} at."));
        }

        var trackingEvent = new CarrierTrackingEvent(
            Guid.CreateVersion7(time.GetUtcNow()).ToString("N"), reference, request.CarrierBookingNumber, command.Type, time.GetUtcNow(), place, postcode, country);
        var reply = await partnerApi.SubmitTrackingAsync(carrier, request.ShipmentNumber, trackingEvent, cancellationToken);

        // An exception leaves the Shipment as it is, so there is nothing to keep.
        if (!reply.IsAccepted || !request.ReportTracking(command.Type, time.GetUtcNow()) || !TrackingRules.IsProgress(command.Type))
        {
            return new SentTrackingEvent(CarrierShipmentView.From(request), trackingEvent, reply);
        }

        try
        {
            await repository.UpdateAsync(request, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Norvia has the event, and that stands: losing the portal's own record of it mustn't hide it.
            using (logger.BeginScope(TrackingEventTracking.KeysOf(carrier, request.ShipmentNumber, trackingEvent, reply.MessageId)))
            {
                LogNotRecorded(exception, carrier.Id.Value, request.CarrierBookingNumber, command.Type);
            }

            return new SentTrackingEvent(CarrierShipmentView.From(request), trackingEvent, reply, Recorded: false);
        }

        return new SentTrackingEvent(CarrierShipmentView.From(request), trackingEvent, reply);
    }

    // Goods are collected, and fail to be collected, at the collection address, which the Carrier also departs from and where
    // it finds goods damaged; they arrive, go out for delivery, and are delivered, or fail to be, at the delivery address.
    private static CarrierBookingStop LocationOf(CarrierBookingDetails details, TrackingEventType type) => type switch
    {
        TrackingEventType.Collected or TrackingEventType.Departed or TrackingEventType.CollectionFailed or TrackingEventType.Damaged => details.Collection,
        _ => details.Delivery,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Norvia accepted Persona {PersonaId}'s {Type} event for {CarrierBookingNumber}, but the portal couldn't record it")]
    private partial void LogNotRecorded(Exception exception, string personaId, string carrierBookingNumber, TrackingEventType type);
}
