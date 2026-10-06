namespace Norvia.Tms.Domain.Exceptions;

public enum ExceptionStatus
{
    /// <summary>Opened by the TMS; it waits for the Planner.</summary>
    Open = 1,

    /// <summary>The Planner resolved it with an outcome and a remark. Final.</summary>
    Resolved,
}
