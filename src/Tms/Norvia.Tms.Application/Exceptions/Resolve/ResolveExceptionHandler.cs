using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Application.Billing;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.Resolve;

public sealed class ResolveExceptionHandler(
    IRepository<ExceptionCase> exceptions,
    IReadRepository<Shipment> shipments,
    BillingCheck billing,
    TimeProvider time)
    : ICommandHandler<ResolveExceptionCommand, Result<ResolveExceptionResult>>
{
    public async ValueTask<Result<ResolveExceptionResult>> Handle(ResolveExceptionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExceptionNumber.TryParse(command.ExceptionNumber, out var number)
            || await exceptions.SingleOrDefaultAsync(new ExceptionToResolveSpec(number), cancellationToken) is not { } exception)
        {
            return ExceptionRefused.Because<ResolveExceptionResult>(ExceptionRefusal.NotFound(command.ExceptionNumber));
        }

        var correlationId = new CorrelationId(command.CorrelationId);
        var now = time.GetUtcNow();
        if (exception.Resolve(command.Outcome, command.Remark, correlationId, now) is { } refusal)
        {
            return ExceptionRefused.Because<ResolveExceptionResult>(refusal);
        }

        // Resolving the last open Exception, whatever the outcome, can complete the conditions of the Transport Order to be Billable (P9).
        if (await shipments.SingleOrDefaultAsync(new ShipmentByNumberSpec(exception.ShipmentNumber), cancellationToken) is { } shipment)
        {
            await billing.CheckAsync(shipment, new BillingChange(correlationId, now, ExceptionResolved: exception.Id), cancellationToken);
        }

        // Saving the tracked Exception stores ExceptionResolved's outbox message, and TransportOrderBillable's if the order became Billable, with it, in one transaction.
        await exceptions.SaveChangesAsync(cancellationToken);
        return new ResolveExceptionResult(exception.Number.Value, exception.ShipmentNumber.Value, exception.Status, command.Outcome, command.CorrelationId);
    }
}
