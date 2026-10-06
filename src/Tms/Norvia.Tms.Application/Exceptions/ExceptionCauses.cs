using Ardalis.SharedKernel;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Exceptions;

/// <summary>
/// Puts a cause of an Exception (P7) on its Shipment: a Shipment has at most one open Exception of each type, so a cause
/// of a type that is open joins it and publishes nothing, and any other opens a new Exception and raises ExceptionOpened.
/// It saves nothing: the caller's save carries the Exception, in the transaction of the Tracking event or Proof of delivery.
/// </summary>
public sealed class ExceptionCauses(IRepository<ExceptionCase> exceptions, IExceptionNumbers exceptionNumbers)
{
    public async Task OpenOrAddAsync(Shipment shipment, ExceptionCause cause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(cause);
        if (await exceptions.SingleOrDefaultAsync(new OpenExceptionOfTypeForShipmentSpec(shipment.Id, cause.Type), cancellationToken) is { } open)
        {
            open.AddCause(cause);
            return;
        }

        await exceptions.AddAsync(ExceptionCase.Open(await exceptionNumbers.NextAsync(cancellationToken), shipment, cause), cancellationToken);
    }
}
