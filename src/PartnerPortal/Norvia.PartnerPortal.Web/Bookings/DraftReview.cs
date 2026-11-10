using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// What the booking form shows of a Booking draft besides its values: the quote each prefilled field came from, the fields to look at, and the
/// text the assistant did not use. The Shipper handles a flagged field by confirming it or by editing it; until every one is handled the booking
/// is not sent. Whether the booking is valid stays the portal's validator's call, and Norvia's. Lives as long as the form and is never stored.
/// </summary>
public sealed class DraftReview
{
    private readonly IReadOnlyDictionary<string, string> _quotes;
    private readonly Dictionary<string, string> _initial;
    private readonly List<DraftFlag> _flags;
    private readonly HashSet<string> _handled = new(StringComparer.Ordinal);

    public DraftReview(BookingDraftAnswer draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _quotes = draft.Quotes;
        _initial = DraftFieldValues.Of(draft.Booking ?? new BookingRequest());
        _flags = [.. draft.Flags
            .DistinctBy(flag => flag.Field, StringComparer.Ordinal)
            .Select(flag => new DraftFlag(flag.Field, flag.Reason, _initial.ContainsKey(flag.Field) ? DraftFlagKind.CheckThis : DraftFlagKind.Missing))];
        Ignored = draft.Ignored;
    }

    /// <summary>Raised when a flag gets handled, so the screen can show it.</summary>
    public event Action? Changed;

    public IReadOnlyList<DraftFlag> Flags => _flags;

    /// <summary>The text the assistant did not use for the booking, such as a signature or a disclaimer.</summary>
    public IReadOnlyList<string> Ignored { get; }

    public IReadOnlyList<DraftFlag> Unhandled => [.. _flags.Where(flag => !_handled.Contains(flag.Field))];

    public bool CanSubmit => Unhandled.Count == 0;

    public string? QuoteOf(string field) => _quotes.GetValueOrDefault(field);

    public DraftFlag? FlagOf(string field) => _flags.Find(flag => flag.Field == field);

    public bool IsHandled(string field) => _handled.Contains(field);

    public void Confirm(string field)
    {
        if (FlagOf(field) is not null && _handled.Add(field))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Takes the form as it is now: a flagged field the Shipper has changed since the draft (filled in, edited, or its goods line removed) is
    /// handled, and stays handled. A flag on the whole booking has no field to edit, so it needs a confirmation.
    /// </summary>
    public void Observe(BookingRequest current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var now = DraftFieldValues.Of(current);
        var handledNow = false;
        foreach (var flag in _flags.Where(flag => flag.Field != "booking" && !_handled.Contains(flag.Field)))
        {
            var before = _initial.GetValueOrDefault(flag.Field);
            var after = now.GetValueOrDefault(flag.Field);
            var lineRemoved = flag.Field.StartsWith("items.", StringComparison.Ordinal) && !DraftFieldValues.HasLine(current, flag.Field);
            if (before != after || lineRemoved)
            {
                handledNow |= _handled.Add(flag.Field);
            }
        }

        if (handledNow)
        {
            Changed?.Invoke();
        }
    }
}
