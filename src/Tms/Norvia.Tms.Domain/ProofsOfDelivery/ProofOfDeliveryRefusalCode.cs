namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>
/// Why the TMS refused a Proof of delivery (P6), for the rules that have a stable code. The names are stable codes that
/// callers and monitoring rely on: never rename one, only add.
/// </summary>
public enum ProofOfDeliveryRefusalCode
{
    /// <summary>No Confirmed Carrier booking of the sending Carrier has the Carrier booking reference.</summary>
    ShipmentNotFound = 1,

    /// <summary>The Shipment isn't Booked or later, so there is nothing yet to prove.</summary>
    ShipmentNotInTransport,

    /// <summary>The Proof of delivery lacks a part the Canonical Model requires. A client error.</summary>
    MissingRequiredField,

    /// <summary>The delivery time is in the future, beyond the clock-skew allowance. A client error.</summary>
    DeliveryTimeInTheFuture,

    /// <summary>The files aren't 1 to 5, each 10 MB or less and a PDF, JPEG or PNG by their content.</summary>
    FileNotAccepted,
}
