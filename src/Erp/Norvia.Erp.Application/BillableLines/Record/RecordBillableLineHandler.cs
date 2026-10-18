using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.Shippers;

namespace Norvia.Erp.Application.BillableLines.Record;

public sealed class RecordBillableLineHandler(IRepository<BillableLine> lines, IReadRepository<Shipper> shippers, TimeProvider time)
    : ICommandHandler<RecordBillableLineCommand, Result<RecordBillableLineResult>>
{
    public async ValueTask<Result<RecordBillableLineResult>> Handle(RecordBillableLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var data = command.Line;

        // Idempotent on the Transport Order number: the same content again is the original line, different content is a conflict.
        if (await lines.FirstOrDefaultAsync(new BillableLineByTransportOrderNumberSpec(data.TransportOrderNumber), cancellationToken) is { } existing)
        {
            return existing.HasSameContentAs(data)
                ? new RecordBillableLineResult(BillableLineViews.Of(existing), Repeat: true)
                : BillableLineRefused.Because<RecordBillableLineResult>(BillableLineRefusal.Conflict(data.TransportOrderNumber));
        }

        if (await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(data.ShipperId), cancellationToken) is null)
        {
            return BillableLineRefused.Because<RecordBillableLineResult>(BillableLineRefusal.ShipperNotFound(data.ShipperId.Value));
        }

        var (line, refusal) = BillableLine.Record(data, time.GetUtcNow());
        if (line is null)
        {
            return BillableLineRefused.Because<RecordBillableLineResult>(
                refusal ?? throw new InvalidOperationException($"Billable line {data.TransportOrderNumber} was neither recorded nor refused."));
        }

        await lines.AddAsync(line, cancellationToken);
        return new RecordBillableLineResult(BillableLineViews.Of(line), Repeat: false);
    }
}
