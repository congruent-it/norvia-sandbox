namespace Norvia.Erp.Application.BillableLines.Record;

/// <param name="Line">The recorded line, or the original one for a repeat.</param>
/// <param name="Repeat">True when the Transport Order already had this line.</param>
public sealed record RecordBillableLineResult(BillableLineView Line, bool Repeat);
