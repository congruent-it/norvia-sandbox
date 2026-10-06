using System.Text.Json.Serialization;

namespace Norvia.Tms.Api.ProofsOfDelivery;

/// <summary>
/// The metadata of a Proof of delivery in the Canonical Model, as the Carrier Proof of delivery Logic App sends it in the
/// <c>metadata</c> part of its multipart call, with the files in the <c>files</c> parts. The correlation ID, which API
/// Management creates, is in <c>X-Correlation-ID</c>.
/// </summary>
/// <param name="DocumentId">The sender's own ID for the Proof of delivery, unique per source.</param>
/// <param name="Source">Who sent it. The Carrier comes from API Management's token check (ADR 0005), never from the Carrier's own payload.</param>
/// <param name="CarrierBookingReference">The Carrier's own reference for its booking, which with the Carrier finds the Shipment.</param>
/// <param name="CarrierBookingNumber">The TMS's own Carrier booking number, used only when the reference doesn't match.</param>
/// <param name="ReceivedBy">The name of the person who signed.</param>
/// <param name="DeliveryTime">When the goods were delivered, with the offset of where.</param>
/// <param name="Files">What the canonical metadata says of its <c>files</c> parts, in order. The parts themselves are what the TMS stores and checks.</param>
/// <param name="Condition">The condition's name, <c>Clean</c> or <c>Reservations</c>; a string, so an unknown one is refused with a code of its own.</param>
/// <remarks>
/// What the Canonical Model requires is nullable here and checked by the endpoint, so a missing field is refused with a
/// stable code instead of the framework's bare 400.
/// </remarks>
public sealed record ProofOfDeliveryRequest(
    string? DocumentId = null,
    ProofOfDeliverySourceRequest? Source = null,
    string? CarrierBookingReference = null,
    string? CarrierBookingNumber = null,
    string? ReceivedBy = null,
    DateTimeOffset? DeliveryTime = null,
    ProofOfDeliveryLocationRequest? Location = null,
    string? Condition = null,
    string? Remark = null,
    IReadOnlyList<ProofOfDeliveryFileRequest>? Files = null);

/// <summary>Who sent a Proof of delivery: a Carrier, with its ID, or Own fleet, with none.</summary>
public sealed record ProofOfDeliverySourceRequest([property: JsonRequired] ProofOfDeliverySourceType Type, string? CarrierId = null);

public enum ProofOfDeliverySourceType
{
    Carrier,
    OwnFleet,
}

/// <summary>Where the goods were delivered.</summary>
/// <param name="CountryCode">ISO 3166-1 alpha-2.</param>
public sealed record ProofOfDeliveryLocationRequest(string? Place = null, string? Postcode = null, string? CountryCode = null);

/// <summary>A file of a Proof of delivery as the canonical metadata describes it; its content is the matching part of the call.</summary>
public sealed record ProofOfDeliveryFileRequest(string? Name = null, string? ContentType = null);
