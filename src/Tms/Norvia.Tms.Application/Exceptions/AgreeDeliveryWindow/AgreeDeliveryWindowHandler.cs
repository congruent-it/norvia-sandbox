using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.AgreeDeliveryWindow;

public sealed class AgreeDeliveryWindowHandler(
    IRepository<ExceptionCase> exceptions,
    IRepository<Shipment> shipments,
    IReadRepository<TrackingEvent> trackingEvents,
    ExceptionCauses exceptionCauses,
    TimeProvider time)
    : ICommandHandler<AgreeDeliveryWindowCommand, Result<AgreeDeliveryWindowResult>>
{
    public async ValueTask<Result<AgreeDeliveryWindowResult>> Handle(AgreeDeliveryWindowCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExceptionNumber.TryParse(command.ExceptionNumber, out var number)
            || await exceptions.SingleOrDefaultAsync(new ExceptionToResolveSpec(number), cancellationToken) is not { } exception)
        {
            return ExceptionRefused.Because<AgreeDeliveryWindowResult>(ExceptionRefusal.NotFound(command.ExceptionNumber));
        }

        // An Exception always has its Shipment, so a missing one is corrupt data, not something the Planner can fix.
        var shipment = await shipments.SingleOrDefaultAsync(new ShipmentToChangeSpec(exception.ShipmentNumber), cancellationToken)
            ?? throw new InvalidOperationException($"Exception {exception.Number} has no Shipment {exception.ShipmentNumber}.");

        var key = new IdempotencyKey(command.AgreementKey);
        if (shipment.AgreedDeliveryWindowUnder(key) is { } earlier)
        {
            return new AgreeDeliveryWindowResult(
                exception.Number.Value, shipment.Number.Value, earlier.Earliest, earlier.Latest, earlier.CorrelationId.Value, Repeat: true);
        }

        if (exception.Status != ExceptionStatus.Open)
        {
            return ExceptionRefused.Because<AgreeDeliveryWindowResult>(
                new(ExceptionRefusalCode.ExceptionNotOpen, $"Exception {exception.Number} is {exception.Status}, not Open."));
        }

        var window = new TimeWindow(command.Earliest, command.Latest);
        if (shipment.AgreeDeliveryWindow(window, exception.Number, key, new CorrelationId(command.CorrelationId), time.GetUtcNow()) is { } refusal)
        {
            return ExceptionRefused.Because<AgreeDeliveryWindowResult>(refusal);
        }

        await RecheckLateDeliveryAsync(shipment, window, cancellationToken);

        await exceptions.SaveChangesAsync(cancellationToken);
        return new AgreeDeliveryWindowResult(exception.Number.Value, shipment.Number.Value, command.Earliest, command.Latest, command.CorrelationId);
    }

    // An open Late delivery whose latest ETA now falls inside the window is marked cause cleared.
    private async Task RecheckLateDeliveryAsync(Shipment shipment, TimeWindow window, CancellationToken cancellationToken)
    {
        var events = await trackingEvents.ListAsync(new TrackingEventsForShipmentsSpec([shipment.Id]), cancellationToken);
        if (!LateDeliveryRules.IsEtaLate(ShipmentEta.Of(events, window), window))
        {
            await exceptionCauses.ClearAsync(shipment, ExceptionType.LateDelivery, cancellationToken);
        }
    }
}
