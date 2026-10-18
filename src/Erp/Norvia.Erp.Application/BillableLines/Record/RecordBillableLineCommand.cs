using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Application.BillableLines.Record;

/// <summary>Records the Billable line <c>TransportOrderBillable</c> carries. Publishes nothing.</summary>
public sealed record RecordBillableLineCommand(BillableLineData Line) : ICommand<Result<RecordBillableLineResult>>;
