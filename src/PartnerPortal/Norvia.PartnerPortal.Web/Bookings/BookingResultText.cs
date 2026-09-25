using Norvia.PartnerPortal.Domain.Bookings;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>A booking's result as the list of bookings names it.</summary>
public static class BookingResultText
{
    public static string Of(BookingResult result) => result switch
    {
        BookingResult.Accepted => "Accepted",
        BookingResult.AlreadyBooked => "Already booked",
        BookingResult.Rejected => "Rejected",
        BookingResult.NotReceived => "Not received",
        BookingResult.Refused => "Refused",
        _ => result.ToString(),
    };
}
