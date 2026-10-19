namespace Norvia.Erp.Domain.BillableLines;

public enum BillableLineStatus
{
    /// <summary>No Invoice run has invoiced the line yet.</summary>
    Waiting,

    /// <summary>An Invoice run put the line on a Shipper invoice, whose number the line holds.</summary>
    Invoiced,
}
