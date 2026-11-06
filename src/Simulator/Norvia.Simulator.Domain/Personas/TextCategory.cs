namespace Norvia.Simulator.Domain.Personas;

/// <summary>The kind of free text a Carrier sends, which a <see cref="TextPool"/> holds the pool of.</summary>
public enum TextCategory
{
    /// <summary>The text of a decline with the reason <c>OTHER</c>.</summary>
    DeclineReason,

    /// <summary>The remark on a delivery failed Tracking event.</summary>
    DeliveryFailedRemark,
}
