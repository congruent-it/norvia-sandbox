using Norvia.Tms.Domain.ProofsOfDelivery;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

/// <summary>A Shipment's Proof of delivery as the API shows it, with its files, which are read through <see cref="ProofOfDeliveryFileView.Id"/>.</summary>
/// <param name="DeliveryTime">When the goods were delivered, with the offset of where.</param>
/// <param name="Source">The Carrier ID, or Own fleet.</param>
public sealed record ProofOfDeliveryView(
    Guid Id,
    string ShipmentNumber,
    string Source,
    string DocumentId,
    string ReceivedBy,
    DateTimeOffset DeliveryTime,
    string Place,
    string Postcode,
    string CountryCode,
    DeliveryCondition Condition,
    string? Remark,
    DateTimeOffset ReceivedAt,
    string CorrelationId,
    IReadOnlyList<ProofOfDeliveryFileView> Files)
{
    public static ProofOfDeliveryView Of(ProofOfDelivery proofOfDelivery)
    {
        ArgumentNullException.ThrowIfNull(proofOfDelivery);
        return new(
            proofOfDelivery.Id.Value,
            proofOfDelivery.ShipmentNumber.Value,
            proofOfDelivery.Source.ToString(),
            proofOfDelivery.DocumentId,
            proofOfDelivery.ReceivedBy,
            proofOfDelivery.DeliveryTime,
            proofOfDelivery.Location.Place,
            proofOfDelivery.Location.Postcode,
            proofOfDelivery.Location.CountryCode,
            proofOfDelivery.Condition,
            proofOfDelivery.Remark,
            proofOfDelivery.ReceivedAt,
            proofOfDelivery.CorrelationId.Value,
            [.. proofOfDelivery.Files.Select(file => new ProofOfDeliveryFileView(file.Id, file.Name, file.ContentType, file.SizeBytes))]);
    }
}

/// <summary>One file of a Proof of delivery.</summary>
public sealed record ProofOfDeliveryFileView(Guid Id, string Name, string ContentType, long SizeBytes);
