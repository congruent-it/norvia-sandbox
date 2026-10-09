namespace Norvia.Tms.Api.Capacity;

/// <summary>An Absence (Id is the Driver)  or an Off the road period (Id is the Vehicle): first and last day, inclusive, a reason by name and a free-text remark.</summary>
public sealed record RecordPeriodRequest(string? Id, DateOnly? From, DateOnly? To, string? Reason, string? Remark);
