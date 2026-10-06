namespace Norvia.Tms.Domain.Exceptions;

/// <summary>Why the TMS refused a Planner's action on an Exception: a stable code and a readable message.</summary>
public sealed record ExceptionRefusal(ExceptionRefusalCode Code, string Message);
