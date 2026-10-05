using Norvia.Tms.Domain.Shipments;
using static System.FormattableString;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>P6's Proof of delivery rules, apart from finding the Shipment by the Carrier's booking reference.</summary>
/// <param name="ClockSkew">How far in the future a delivery time may be, to allow for the sender's clock running ahead.</param>
public sealed record ProofOfDeliveryRules(TimeSpan ClockSkew)
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    public static ProofOfDeliveryRefusal ShipmentNotFound(string carrierBookingReference) =>
        new(ProofOfDeliveryRefusalCode.ShipmentNotFound, Invariant($"No Confirmed Carrier booking of this Carrier has the booking reference {carrierBookingReference}."));

    /// <summary>The refusal for a Shipment that isn't Booked or later, or null when it is.</summary>
    public static ProofOfDeliveryRefusal? CheckShipment(Shipment shipment)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        return shipment.Status >= ShipmentStatus.Booked
            ? null
            : new(ProofOfDeliveryRefusalCode.ShipmentNotInTransport, Invariant($"Shipment {shipment.Number} is {shipment.Status}, not Booked or later."));
    }

    /// <summary>The refusal for a delivery time later than now plus the clock skew, or null when it isn't.</summary>
    public ProofOfDeliveryRefusal? CheckDeliveryTime(DateTimeOffset deliveryTime, DateTimeOffset now) =>
        deliveryTime > now + ClockSkew
            ? new(ProofOfDeliveryRefusalCode.DeliveryTimeInTheFuture, Invariant($"The delivery time {deliveryTime:O} is in the future."))
            : null;

    /// <summary>The refusal for files that aren't 1 to 5, each 10 MB or less and a PDF, JPEG or PNG by content, or null when they are.</summary>
    public static ProofOfDeliveryRefusal? CheckFiles(IReadOnlyList<ProofOfDeliveryFile> files, IReadOnlyList<ReadOnlyMemory<byte>> contents)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(contents);

        if (files.Count is 0 or > MaxFiles)
        {
            return NotAccepted(Invariant($"A Proof of delivery has 1 to {MaxFiles} files, not {files.Count}."));
        }

        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].SizeBytes > MaxFileBytes || contents[i].Length > MaxFileBytes)
            {
                return NotAccepted(Invariant($"File {files[i].Name} is over 10 MB."));
            }

            if (ProofOfDeliveryFileKind.Detect(contents[i].Span) is null)
            {
                return NotAccepted(Invariant($"File {files[i].Name} isn't a PDF, JPEG or PNG."));
            }
        }

        return null;
    }

    /// <summary>The files as stored: each described by what its content is, not by what the sender declared.</summary>
    public static IReadOnlyList<ProofOfDeliveryFile> Judged(IReadOnlyList<ProofOfDeliveryFile> files, IReadOnlyList<ReadOnlyMemory<byte>> contents) =>
        [.. files.Select((file, i) => file with { ContentType = ProofOfDeliveryFileKind.Detect(contents[i].Span) ?? file.ContentType })];

    private static ProofOfDeliveryRefusal NotAccepted(string message) => new(ProofOfDeliveryRefusalCode.FileNotAccepted, message);
}
