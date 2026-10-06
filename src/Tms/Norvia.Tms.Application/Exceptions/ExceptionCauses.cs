using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Application.Exceptions;

/// <summary>
/// Puts a cause of an Exception (P7) on its Shipment: a Shipment has at most one open Exception of each type, so a cause
/// of a type that is open joins it and publishes nothing, and any other opens a new Exception and raises ExceptionOpened.
/// It saves nothing: the caller's save carries the Exception, in the transaction of the Tracking event or Proof of delivery.
/// </summary>
public sealed class ExceptionCauses(IRepository<ExceptionCase> exceptions, IExceptionNumbers exceptionNumbers)
{
    public async Task OpenOrAddAsync(Shipment shipment, TrackingEvent cause, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(cause);
        if (ExceptionTypes.OpenedBy(cause.Type) is not { } type)
        {
            return;
        }

        if (await OpenOfAsync(shipment, type, cancellationToken) is { } open)
        {
            open.AddCause(cause, now);
            return;
        }

        await exceptions.AddAsync(ExceptionCase.Open(await exceptionNumbers.NextAsync(cancellationToken), shipment, cause, now), cancellationToken);
    }

    public async Task OpenOrAddAsync(Shipment shipment, ProofOfDelivery cause, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(cause);
        if (ExceptionTypes.OpenedBy(cause.Condition) is not { } type)
        {
            return;
        }

        if (await OpenOfAsync(shipment, type, cancellationToken) is { } open)
        {
            open.AddCause(cause, now);
            return;
        }

        await exceptions.AddAsync(ExceptionCase.Open(await exceptionNumbers.NextAsync(cancellationToken), shipment, cause, now), cancellationToken);
    }

    private Task<ExceptionCase?> OpenOfAsync(Shipment shipment, ExceptionType type, CancellationToken cancellationToken) =>
        exceptions.SingleOrDefaultAsync(new OpenExceptionOfTypeForShipmentSpec(shipment.Id, type), cancellationToken);
}
