using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Norvia.Tms.Domain.CarrierBookings;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.Fleet;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TrackingEvents;
using Norvia.Tms.Domain.Trips;

namespace Norvia.Tms.Api.ProofsOfDelivery;

/// <summary>
/// Reads the multipart call of a Proof of delivery as the domain takes it: the metadata part as Canonical Model JSON, and
/// the fields it requires, beyond their JSON shape. What it can't read it reports as problems with a stable code, so the
/// endpoint only has to answer them.
/// </summary>
internal static class ProofOfDeliveryReader
{
    private const string MetadataPart = "metadata";
    internal const string FilesPart = "files";

    internal static ReadProblem Missing(string message) => new(ProofOfDeliveryRefusalCode.MissingRequiredField, message);

    /// <summary>The metadata part: JSON in the Canonical Model, as a form field or as a file part.</summary>
    internal static bool TryReadMetadata(
        IFormCollection form,
        JsonSerializerOptions options,
        [NotNullWhen(true)] out ProofOfDeliveryRequest? metadata,
        out ReadProblem problem)
    {
        metadata = null;
        problem = Missing("A Proof of delivery needs its metadata part, as Canonical Model JSON.");
        string? text = form.TryGetValue(MetadataPart, out var field) ? field.ToString() : null;
        if (text is null && form.Files.GetFile(MetadataPart) is { } part)
        {
            using var reader = new StreamReader(part.OpenReadStream());
            text = reader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            metadata = JsonSerializer.Deserialize<ProofOfDeliveryRequest>(text, options);
        }
        catch (JsonException ex)
        {
            problem = Missing($"The metadata part isn't the Canonical Model: {ex.Message}");
            return false;
        }

        return metadata is not null;
    }

    /// <summary>
    /// Reads the Canonical Model metadata as the domain takes it, beyond its JSON shape: the fields it requires, and a
    /// condition the TMS knows. Either returns the report, or every problem found, in the order of the metadata's fields.
    /// </summary>
    internal static bool TryRead(
        ProofOfDeliveryRequest metadata,
        List<ProofOfDeliveryFile> files,
        [NotNullWhen(true)] out ProofOfDeliveryReport? report,
        out IReadOnlyList<ReadProblem> problems)
    {
        var found = new List<ReadProblem>();
        report = null;
        problems = found;

        if (string.IsNullOrWhiteSpace(metadata.DocumentId))
        {
            found.Add(Missing("A Proof of delivery needs its source's own document ID."));
        }

        ProofOfDeliverySource? source = null;
        OwnFleetReport? ownFleet = null;
        if (metadata.Source is not { } sourceRequest)
        {
            found.Add(Missing("A Proof of delivery needs its source."));
        }
        else if (sourceRequest.Type == ProofOfDeliverySourceType.Carrier)
        {
            if (string.IsNullOrWhiteSpace(sourceRequest.CarrierId))
            {
                found.Add(Missing("A Carrier source needs the Carrier ID."));
            }
            else
            {
                source = ProofOfDeliverySource.Carrier(new CarrierId(sourceRequest.CarrierId));
            }

            if (sourceRequest.DriverId is not null || metadata.TripNumber is not null || metadata.ShipmentNumber is not null || metadata.TripVersion is not null)
            {
                found.Add(Missing("A Carrier Proof of delivery names no Driver, Trip or Shipment."));
            }

            if (string.IsNullOrWhiteSpace(metadata.CarrierBookingReference))
            {
                found.Add(Missing("A Proof of delivery needs the Carrier's own booking reference."));
            }
        }
        else if (sourceRequest.CarrierId is not null)
        {
            found.Add(Missing("An Own fleet source names no Carrier."));
        }
        else
        {
            source = ProofOfDeliverySource.OwnFleet;
            ownFleet = ReadOwnFleet(metadata, sourceRequest, found);
        }

        if (string.IsNullOrWhiteSpace(metadata.ReceivedBy))
        {
            found.Add(Missing("A Proof of delivery needs the name of the person who signed."));
        }

        if (metadata.DeliveryTime is null)
        {
            found.Add(Missing("A Proof of delivery needs the time the goods were delivered."));
        }

        TrackingLocation? place = null;
        if (metadata.Location is not { } location || string.IsNullOrWhiteSpace(location.Place) || string.IsNullOrWhiteSpace(location.Postcode))
        {
            found.Add(Missing("A location needs its place and postcode."));
        }
        else if (location.CountryCode is not { Length: 2 } || !location.CountryCode.All(char.IsAsciiLetterUpper))
        {
            found.Add(Missing("A location needs its country as an ISO 3166-1 alpha-2 code."));
        }
        else
        {
            place = new TrackingLocation(location.Place, location.Postcode, location.CountryCode);
        }

        DeliveryCondition? condition = null;
        if (string.IsNullOrWhiteSpace(metadata.Condition))
        {
            found.Add(Missing("A Proof of delivery needs its condition."));
        }
        else if (Enum.TryParse<DeliveryCondition>(metadata.Condition, out var parsed) && Enum.IsDefined(parsed) && !metadata.Condition.All(char.IsAsciiDigit))
        {
            condition = parsed;
        }
        else
        {
            found.Add(Missing($"'{metadata.Condition}' isn't a condition the TMS knows: Clean or Reservations."));
        }

        if (condition == DeliveryCondition.Reservations && string.IsNullOrWhiteSpace(metadata.Remark))
        {
            found.Add(Missing("A Proof of delivery with reservations needs a remark."));
        }

        if (found.Count > 0
            || metadata.DocumentId is not { } documentId
            || source is null
            || (ownFleet is null && source.IsOwnFleet)
            || metadata.ReceivedBy is not { } receivedBy
            || metadata.DeliveryTime is not { } deliveryTime
            || place is null
            || condition is not { } knownCondition)
        {
            return false;
        }

        report = new ProofOfDeliveryReport(
            source, documentId, metadata.CarrierBookingReference, receivedBy, deliveryTime, place, knownCondition, metadata.Remark, files,
            CarrierBookingNumber.TryParse(metadata.CarrierBookingNumber, out var number) ? number : null, ownFleet);
        return true;
    }

    // An Own fleet Proof of delivery is about a Trip, a Shipment on it and a Driver, and names no Carrier booking.
    private static OwnFleetReport? ReadOwnFleet(ProofOfDeliveryRequest metadata, ProofOfDeliverySourceRequest source, List<ReadProblem> found)
    {
        var before = found.Count;
        if (!TripNumber.TryParse(metadata.TripNumber, out var tripNumber))
        {
            found.Add(Missing("An Own fleet Proof of delivery needs the number of its Trip, such as TR-0000042."));
        }

        if (!ShipmentNumber.TryParse(metadata.ShipmentNumber, out var shipmentNumber))
        {
            found.Add(Missing("An Own fleet Proof of delivery needs the number of its Shipment, such as SH-0000042."));
        }

        if (metadata.TripVersion is not >= 0)
        {
            found.Add(Missing("An Own fleet Proof of delivery needs the version of the Trip the Driver app held."));
        }

        if (string.IsNullOrWhiteSpace(source.DriverId))
        {
            found.Add(Missing("An Own fleet source needs the Driver ID from the Driver's token."));
        }

        if (metadata.CarrierBookingReference is not null || metadata.CarrierBookingNumber is not null)
        {
            found.Add(Missing("An Own fleet Proof of delivery names no Carrier booking."));
        }

        return found.Count > before || tripNumber is null || shipmentNumber is null || source.DriverId is not { } driverId || metadata.TripVersion is not { } version
            ? null
            : new OwnFleetReport(tripNumber, new DriverId(driverId), shipmentNumber, version);
    }

    /// <summary>One thing wrong with a Proof of delivery the TMS can't read, with the stable code of its kind.</summary>
    internal readonly record struct ReadProblem(ProofOfDeliveryRefusalCode Code, string Message);
}
