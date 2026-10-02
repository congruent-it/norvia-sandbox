namespace Norvia.Tms.Web.Shipments;

/// <summary>What the TMS API answered a withdrawal of a Carrier booking.</summary>
public abstract record WithdrawAnswer
{
    private WithdrawAnswer()
    {
    }

    /// <summary>The Carrier booking is Withdrawn now and its Shipment is Planned again, so the booking leaves the list of bookings awaiting an answer.</summary>
    public sealed record Withdrawn : WithdrawAnswer;

    /// <summary>The TMS didn't withdraw it, such as a booking the Carrier answered already, with its readable reason.</summary>
    public sealed record Refused(string Reason) : WithdrawAnswer;
}
