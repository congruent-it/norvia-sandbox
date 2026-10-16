namespace Norvia.Simulator.Application.Reset;

/// <summary>Whether the estate would take a reset now, and if not, the first reason why.</summary>
public sealed record EstateResetReadiness(bool RunActive, IReadOnlyList<AppReadiness> Apps)
{
    /// <summary>Why a reset can't be taken now, or null when it can. A run comes first, then each application in turn.</summary>
    public string? Reason
    {
        get
        {
            if (RunActive)
            {
                return "A run is active. Cancel it or wait for it to finish.";
            }

            foreach (var app in Apps)
            {
                var name = EstateApps.NameOf(app.App);
                if (!app.Reachable)
                {
                    return $"The {name} couldn't be asked whether it would take a reset.";
                }

                if (!app.Enabled)
                {
                    return $"The reset is switched off for the {name}.";
                }

                if (app.BusySubscription is { } busy)
                {
                    return $"The subscription {busy} holds unprocessed messages. Try again when it is empty.";
                }
            }

            return null;
        }
    }
}
