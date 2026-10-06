using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.Resolve;

public sealed class ResolveExceptionHandler(IRepository<ExceptionCase> exceptions, TimeProvider time)
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

        if (exception.Resolve(command.Outcome, command.Remark, new CorrelationId(command.CorrelationId), time.GetUtcNow()) is { } refusal)
        {
            return ExceptionRefused.Because<ResolveExceptionResult>(refusal);
        }

        // Saving the tracked Exception stores ExceptionResolved's outbox message with it, in one transaction.
        await exceptions.SaveChangesAsync(cancellationToken);
        return new ResolveExceptionResult(exception.Number.Value, exception.ShipmentNumber.Value, exception.Status, command.Outcome, command.CorrelationId);
    }
}
