using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Application.Exceptions.AddNote;

public sealed class AddExceptionNoteHandler(IRepository<ExceptionCase> exceptions, TimeProvider time)
    : ICommandHandler<AddExceptionNoteCommand, Result<AddExceptionNoteResult>>
{
    public async ValueTask<Result<AddExceptionNoteResult>> Handle(AddExceptionNoteCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ExceptionNumber.TryParse(command.ExceptionNumber, out var number)
            || await exceptions.SingleOrDefaultAsync(new ExceptionToResolveSpec(number), cancellationToken) is not { } exception)
        {
            return ExceptionRefused.Because<AddExceptionNoteResult>(ExceptionRefusal.NotFound(command.ExceptionNumber));
        }

        if (exception.AddNote(command.Text, command.Planner, new CorrelationId(command.CorrelationId), time.GetUtcNow()) is { } refusal)
        {
            return ExceptionRefused.Because<AddExceptionNoteResult>(refusal);
        }

        await exceptions.SaveChangesAsync(cancellationToken);
        return new AddExceptionNoteResult(exception.Number.Value, exception.ShipmentNumber.Value, exception.Notes.Count, command.CorrelationId);
    }
}
