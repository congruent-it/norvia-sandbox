namespace Norvia.PartnerPortal.Application.Faults;

/// <summary>The fault switches the applications own, which the presenter panel operates. Each application is its own application.</summary>
public interface IFaultSwitches
{
    /// <summary>Every fault of every application that owns one, with its state now.</summary>
    Task<IReadOnlyList<FaultView>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Switches a fault on for <paramref name="duration"/>, or off when it is null, and returns how it now stands.</summary>
    Task<FaultView> SwitchAsync(string owner, string name, TimeSpan? duration, CancellationToken cancellationToken);
}
