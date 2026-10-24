using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.PartnerPortal.Infrastructure.Faults;

/// <summary>
/// The faults of every application, for the presenter panel: the portal's own it switches in process, because the panel is the
/// presenter's gate already and a fault endpoint on the portal would need a token gate of its own; the others it switches
/// through <see cref="FaultSwitchClient"/>.
/// </summary>
internal sealed partial class PartnerPortalFaultSwitches(FaultSwitchClient others, IFaults own, ILogger<PartnerPortalFaultSwitches> logger) : IFaultSwitches
{
    private const string Flow = "fault-switch";

    public async Task<IReadOnlyList<FaultView>> ListAsync(CancellationToken cancellationToken)
    {
        var faults = new List<FaultView>(await others.ListAsync(cancellationToken));
        faults.AddRange(own.List().Select(ViewOf));
        return [.. faults.OrderBy(fault => fault.Owner, StringComparer.Ordinal).ThenBy(fault => fault.Name, StringComparer.Ordinal)];
    }

    public async Task<FaultView> SwitchAsync(string owner, string name, TimeSpan? duration, CancellationToken cancellationToken)
    {
        if (owner != PartnerPortalFaultDefinitions.Owner)
        {
            return await others.SwitchAsync(owner, name, duration, cancellationToken);
        }

        FaultStatus? status;
        var known = duration is { } length
            ? own.SwitchOn(name, length, out status) switch
            {
                SwitchOnResult.Switched => true,
                SwitchOnResult.UnknownFault => false,
                _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration must be more than zero and no longer than the portal allows."),
            }
            : own.SwitchOff(name, out status);
        if (!known)
        {
            throw new ArgumentException($"The Partner portal owns no fault '{name}'.", nameof(name));
        }

        var view = ViewOf(status!);
        // The presenter panel changes the portal's own state, so it does it on a trail of its own (coding guidelines §7).
        var keys = new Dictionary<string, object?>
        {
            ["correlationId"] = Guid.NewGuid().ToString(),
            ["flow"] = Flow,
            ["fault"] = name,
            ["outcome"] = "Succeeded",
        };
        using (logger.BeginScope(keys))
        {
            LogSwitched(name, view.On);
        }

        return view;
    }

    private static FaultView ViewOf(FaultStatus status) => new(status.Name, status.Owner, status.Description, status.On, status.Until);

    [LoggerMessage(Level = LogLevel.Information, Message = "Switched the fault {Fault} on: {On}")]
    private partial void LogSwitched(string fault, bool on);
}
