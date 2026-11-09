using Norvia.PartnerPortal.Application.Bookings.DraftFromText;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// Holds the Booking draft from "Fill from an email" until the booking form takes it. Scoped to the circuit and never stored: the draft is not a
/// booking, has no link back from one, and is gone once the form has it or the Shipper closes the page.
/// </summary>
public sealed class BookingDraftHandoff
{
    private BookingDraftAnswer? _draft;

    public void Put(BookingDraftAnswer draft) => _draft = draft;

    /// <summary>The draft, once: the form that takes it empties the handoff.</summary>
    public BookingDraftAnswer? Take()
    {
        var draft = _draft;
        _draft = null;
        return draft;
    }
}
