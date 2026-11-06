namespace Norvia.Simulator.Domain.Personas;

/// <summary>The kind of free text a Carrier or a Driver sends, which a <see cref="TextPool"/> holds the pool of.</summary>
public enum TextCategory
{
    /// <summary>The text of a decline with the reason <c>OTHER</c>.</summary>
    DeclineReason,

    /// <summary>The remark on a delivery failed Tracking event.</summary>
    DeliveryFailedRemark,

    /// <summary>The remark of a Driver on a Collection failed report.</summary>
    DriverCollectionFailedRemark,

    /// <summary>The remark of a Driver on a Delivery failed report.</summary>
    DriverDeliveryFailedRemark,

    /// <summary>The remark of a Driver on a Report damage report.</summary>
    DriverDamageRemark,
}
