namespace Norvia.PartnerPortal.Application.Faults;

/// <summary>A runtime fault an application owns, and whether it is on now.</summary>
/// <param name="Owner">The application the fault breaks, such as <c>driver-app</c>.</param>
/// <param name="Until">When the fault expires by itself, or null while it is off.</param>
public sealed record FaultView(string Name, string Owner, string Description, bool On, DateTimeOffset? Until)
{
    /// <summary>How long the fault has left at <paramref name="now"/>, or null when it is off or has run out.</summary>
    public TimeSpan? RemainingAt(DateTimeOffset now) => On && Until is { } until && until > now ? until - now : null;
}
