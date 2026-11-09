namespace Norvia.Agents.Scripts;

/// <summary>
/// Synthetic emails the scripted Intake agent replays a draft for (ADR 0041). They hold no real person or company. Paste one into the
/// Partner portal's "Fill from an email" screen to see the local flow.
/// </summary>
public static class BookingIntakeSamples
{
    /// <summary>An English email that gives everything a booking needs.</summary>
    public const string Good =
        "Hi Norvia,\n\n" +
        "Please collect 2 pallets (850 kg in total, 120x80x150 cm each) at Havenstraat 12, 3011 AB Rotterdam, Netherlands on 14-11-2030 between 08:00 and 12:00.\n" +
        "Deliver to Mueller GmbH, Lindenallee 5, 50667 Koeln, Germany. Contact: Anna Mueller, +49 221 555 0100.\n" +
        "Our reference is SH-2030-0142.\n\nThanks,\nJo";

    /// <summary>A Dutch email that leaves most things out and gives a weight that is no weight.</summary>
    public const string Partial =
        "Beste Norvia,\n\n" +
        "Kunnen jullie morgen 3 pallets ophalen bij ons magazijn in Utrecht en naar Antwerpen brengen? " +
        "Het gaat om zware spullen, ongeveer een ton.\n\nGroet,\nJan";

    /// <summary>An email that asks for two bookings, which the agent refuses.</summary>
    public const string TwoBookings =
        "Hello,\n\nFirst booking: 1 pallet from Utrecht to Antwerp on 14-11-2030.\n" +
        "Second booking: 4 crates from Breda to Gent on 15-11-2030.\n\nThanks";
}
