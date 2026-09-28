namespace Norvia.Tms.Domain.Shipments;

/// <summary>The assignment rule a Planner's assignment broke: its stable code and a plain-language message.</summary>
public sealed record AssignmentRefusal(AssignmentRefusalCode Code, string Message);
