using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The Driver app API, the door a Driver works a Trip through (ADR 0039), each call as that Driver with the Driver ID as the token's role. The Driver app
/// unreachable is an answer, not an error: <see cref="Domain.Log.LogOutcome.DriverNotReceived"/>.
/// </summary>
public interface IDriverApp
{
    /// <summary>The Driver's Trips as the Driver app shows them, or null when it couldn't be asked.</summary>
    Task<IReadOnlyList<DriverTripSnapshot>?> GetTripsAsync(SimulatedDriver driver, CancellationToken cancellationToken);

    Task<DriverCallAnswer> StartTripAsync(SimulatedDriver driver, string tripNumber, CancellationToken cancellationToken);

    /// <summary>Collected at a collection Stop.</summary>
    Task<DriverCallAnswer> CollectAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, CancellationToken cancellationToken);

    // The remark calls take the tracking contract's `profile` key as their last argument: `hostile` when the remark is the opt-in hostile profile's, left out otherwise.

    /// <summary>Collection failed at a collection Stop, with a remark.</summary>
    Task<DriverCallAnswer> FailCollectionAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, string remark, CancellationToken cancellationToken, string? profile = null);

    /// <summary>Delivery failed at a delivery Stop, with a remark.</summary>
    Task<DriverCallAnswer> FailDeliveryAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, string remark, CancellationToken cancellationToken, string? profile = null);

    /// <summary>Damage found at a Stop, with a remark. It doesn't give the Stop an outcome.</summary>
    Task<DriverCallAnswer> ReportDamageAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, bool atDelivery, string remark, CancellationToken cancellationToken, string? profile = null);

    /// <summary>Delivered at a delivery Stop, with the Proof of delivery.</summary>
    Task<DriverCallAnswer> DeliverAsync(SimulatedDriver driver, string tripNumber, string shipmentNumber, DriverDelivery delivery, CancellationToken cancellationToken);

    Task<DriverCallAnswer> EndTripAsync(SimulatedDriver driver, string tripNumber, CancellationToken cancellationToken);
}
