namespace Norvia.Tms.Web.Exceptions;

/// <summary>What the TMS API answered a Planner's action on an Exception: notifying the Shipper, cancelling the Carrier booking, agreeing a delivery window, adding a note or resolving it.</summary>
public abstract record ExceptionAnswer
{
    private ExceptionAnswer()
    {
    }

    /// <summary>The TMS did what the Planner asked.</summary>
    public sealed record Done : ExceptionAnswer;

    /// <summary>The TMS didn't, such as an Exception someone resolved meanwhile, with its readable reason.</summary>
    public sealed record Refused(string Reason) : ExceptionAnswer;
}
