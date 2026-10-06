namespace Norvia.Tms.Domain.Exceptions;

/// <summary>
/// Why the TMS refused a Planner's action on an Exception (P7), for the rules that have a stable code. The names are
/// stable codes that callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum ExceptionRefusalCode
{
    /// <summary>The TMS has no Exception with that number.</summary>
    ExceptionNotFound = 1,

    /// <summary>The Exception is resolved already.</summary>
    ExceptionNotOpen,
}
