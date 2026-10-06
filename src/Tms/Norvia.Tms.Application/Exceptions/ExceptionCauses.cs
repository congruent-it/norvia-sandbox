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

        await OpenAsync(shipment, cause, cancellationToken);
    }

    private async Task OpenAsync(Shipment shipment, ExceptionCause cause, CancellationToken cancellationToken) =>
        await exceptions.AddAsync(ExceptionCase.Open(await exceptionNumbers.NextAsync(cancellationToken), shipment, cause), cancellationToken);

    /// <summary>
    /// For a check that runs again and again and finds the same thing: opens an Exception for the cause unless the Shipment has
    /// an open one of the type already. That one gains the cause only when its cause was cleared, which removes the marker. It
    /// saves nothing, like <see cref="OpenOrAddAsync"/>.
    /// </summary>
    public async Task OpenUnlessOpenAsync(Shipment shipment, ExceptionCause cause, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(cause);
        if (await exceptions.SingleOrDefaultAsync(new OpenExceptionOfTypeForShipmentSpec(shipment.Id, cause.Type), cancellationToken) is not { } open)
        {
            await OpenAsync(shipment, cause, cancellationToken);
        }
        else if (open.CauseCleared)
        {
            open.AddCause(cause);
        }
    }

    /// <summary>
    /// Marks the cause of the Shipment's open Exception of the type cleared, if it has one. It publishes nothing and saves
    /// nothing, like <see cref="OpenOrAddAsync"/>.
    /// </summary>
    public async Task ClearAsync(Shipment shipment, ExceptionType type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        if (await exceptions.SingleOrDefaultAsync(new OpenExceptionOfTypeForShipmentSpec(shipment.Id, type), cancellationToken) is { } open)
        {
            open.MarkCauseCleared();
        }
    }
}
