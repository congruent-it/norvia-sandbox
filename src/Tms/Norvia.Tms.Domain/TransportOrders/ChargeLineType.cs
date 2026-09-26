namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>What a charge line of a Freight Charge charges for. The names are stable: never rename one, only add.</summary>
public enum ChargeLineType
{
    /// <summary>The weight ÷ 100 × the Weight Break's price per 100 kg.</summary>
    Freight = 1,

    /// <summary>What makes the Freight up to the Lane's minimum charge, when the Freight is below it.</summary>
    MinimumChargeAdjustment,
}
