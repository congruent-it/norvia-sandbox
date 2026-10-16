namespace Norvia.DriverApp.Api.Actions;

/// <param name="Remark">What the Driver says. Required for a failure and for damage.</param>
public sealed record StopReportRequest(string? Remark);
