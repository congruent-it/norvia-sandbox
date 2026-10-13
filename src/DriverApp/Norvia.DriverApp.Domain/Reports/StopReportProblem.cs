namespace Norvia.DriverApp.Domain.Reports;

/// <summary>Why a report on a Stop cannot be made as given: the field at fault and what is wrong with it.</summary>
public sealed record StopReportProblem(string Field, string Message);
