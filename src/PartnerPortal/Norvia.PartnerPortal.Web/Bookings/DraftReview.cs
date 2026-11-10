using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;

namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>
/// What the booking form shows of a Booking draft besides its values: the quote each prefilled field came from, the fields to look at, and the
/// text the Intake agent did not use. The Shipper handles a flagged field by confirming it or by editing it; until every one is handled the booking
/// is not sent. Whether the booking is valid stays the portal's validator's call, and Norvia's. Lives as long as the form and is never stored.
/// </summary>
public sealed class DraftReview
{
    private readonly IReadOnlyDictionary<string, string> _quotes;
    private readonly Dictionary<string, string> _initial;
    private readonly List<DraftFlag> _flags;
    private readonly HashSet<string> _confirmed = new(StringComparer.Ordinal);
    private HashSet<string> _edited = new(StringComparer.Ordinal);

    public DraftReview(BookingDraftAnswer draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        _quotes = draft.Quotes;
        _initial = DraftFieldValues.Of(draft.Booking ?? new BookingRequest());
        _flags = [.. draft.Flags
            .DistinctBy(flag => flag.Field, StringComparer.Ordinal)
            .Select(flag => new DraftFlag(flag.Field, flag.Reason, flag.CheckThis ? DraftFlagKind.CheckThis : DraftFlagKind.Missing))];
        Ignored = draft.Ignored;
    }

    /// <summary>Raised when the set of fields still to handle changes, so the screen can show it.</summary>
    public event Action? Changed;

    public IReadOnlyList<DraftFlag> Flags => _flags;

    /// <summary>The text the Intake agent did not use for the booking, such as a signature or a disclaimer.</summary>
    public IReadOnlyList<string> Ignored { get; }

    public IReadOnlyList<DraftFlag> Unhandled => [.. _flags.Where(flag => !IsHandled(flag.Field))];

    public bool CanSubmit => Unhandled.Count == 0;

    public string? QuoteOf(string field) => _quotes.GetValueOrDefault(field);

    public DraftFlag? FlagOf(string field) => _flags.Find(flag => flag.Field == field);

    public bool IsHandled(string field) => _confirmed.Contains(field) || _edited.Contains(field);

    public void Confirm(string field)
    {
        if (FlagOf(field) is not null && !IsHandled(field))
        {
            _confirmed.Add(field);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Takes the form as it is now: a flagged field that differs from the draft (filled in, edited, or its goods line removed) is handled for as
    /// long as it differs, so putting the draft's value back unhandles it; a confirmation stays. A flag on the whole booking has no field to
    /// edit, so it needs a confirmation.
    /// </summary>
    public void Observe(BookingRequest current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var now = DraftFieldValues.Of(current);
        var before = Unhandled.Count;
        _edited = [.. _flags
            .Where(flag => flag.Field != "booking")
            .Where(flag => _initial.GetValueOrDefault(flag.Field) != now.GetValueOrDefault(flag.Field)
                || (flag.Field.StartsWith("items.", StringComparison.Ordinal) && !DraftFieldValues.HasLine(current, flag.Field)))
            .Select(flag => flag.Field)];

        if (Unhandled.Count != before)
        {
            Changed?.Invoke();
        }
    }
}
