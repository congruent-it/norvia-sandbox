namespace Norvia.ServiceDefaults.Faults;

/// <summary>
/// The faults an application owns. Every fault is off by default and, once switched on, expires by itself. The state is the
/// application's own, held in the process, so the same code runs locally and in Azure; a restart switches every fault off.
/// </summary>
public interface IFaults
{
    /// <summary>Whether the fault is on now. An unknown fault is off.</summary>
    bool IsOn(string name);

    /// <summary>Every fault the application owns, with its state now.</summary>
    IReadOnlyList<FaultStatus> List();

    /// <summary>Switches a fault on for <paramref name="duration"/>, or for <see cref="FaultOptions.DefaultDuration"/> when it is null.</summary>
    SwitchOnResult SwitchOn(string name, TimeSpan? duration, out FaultStatus? status);

    /// <summary>Switches a fault off. Returns false when the application owns no fault of that name.</summary>
    bool SwitchOff(string name, out FaultStatus? status);
}
