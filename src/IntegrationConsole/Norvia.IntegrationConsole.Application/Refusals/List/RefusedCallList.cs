namespace Norvia.IntegrationConsole.Application.Refusals.List;

/// <summary>The calls refused between <paramref name="From"/> and <paramref name="To"/>.</summary>
/// <param name="Operations">Every operation with a refusal in the window, whatever the query narrowed to.</param>
/// <param name="Groups">The shown calls grouped by caller and status, most refused first.</param>
/// <param name="Calls">Newest first.</param>
public sealed record RefusedCallList(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<string> Operations, IReadOnlyList<RefusalGroup> Groups, IReadOnlyList<RefusedCall> Calls);
