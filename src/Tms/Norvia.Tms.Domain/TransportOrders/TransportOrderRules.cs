using System.Diagnostics.CodeAnalysis;
using Norvia.Tms.Domain.Shippers;
using static System.FormattableString;

namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// The rules a booking must meet to become an Accepted Transport Order. Every rule is checked, so a rejected
/// booking gets every reason at once.
/// </summary>
public sealed class TransportOrderRules(decimal maxTotalWeightKg)
{
    /// <summary>The package types a goods line may have.</summary>
    public static readonly IReadOnlyList<string> PackageTypes = ["pallet", "parcel", "crate", "loose"];

    /// <summary>The longest Shipper reference the TMS keeps as a key. Chosen well above any real reference.</summary>
    public const int MaxShipperReferenceLength = 100;

    public decimal MaxTotalWeightKg { get; } = maxTotalWeightKg;

    /// <param name="shipper">The Shipper the booking names, or null when the TMS doesn't know it.</param>
    /// <param name="referenceHolder">The Accepted Transport Order that already holds the booking's Shipper reference, if any.</param>
    public IReadOnlyList<RejectionReason> Check(
        Shipper? shipper,
        TransportOrderDetails details,
        DateTimeOffset now,
        TransportOrder? referenceHolder = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        var reasons = new List<RejectionReason>();

        CheckShipper(shipper, details, reasons);
        CheckShipperReference(details, referenceHolder, reasons);
        CheckAddresses(details, reasons);
        CheckWindows(details, now, reasons);
        CheckGoodsLines(details, reasons);
        CheckTotalWeight(details, reasons);

        return reasons;
    }

    private static void CheckShipper(Shipper? shipper, TransportOrderDetails details, List<RejectionReason> reasons)
    {
        if (shipper is null)
        {
            reasons.Add(new(RejectionCode.UnknownShipper, "The Shipper is not known to Norvia."));
        }
        else if (!shipper.Active)
        {
            reasons.Add(new(RejectionCode.InactiveShipper, "The Shipper is not active, so Norvia can't take bookings for it."));
        }
    }

    private static void CheckShipperReference(TransportOrderDetails details, TransportOrder? referenceHolder, List<RejectionReason> reasons)
    {
        if (IsBlank(details.ShipperReference))
        {
            reasons.Add(new(RejectionCode.ShipperReferenceMissing, "The booking has no Shipper reference."));
        }
        else if (details.ShipperReference.Length > MaxShipperReferenceLength)
        {
            reasons.Add(new(RejectionCode.ShipperReferenceTooLong, Invariant(
                $"The Shipper reference is {details.ShipperReference.Length} characters long; it may be at most {MaxShipperReferenceLength}.")));
        }

        if (referenceHolder is not null && !referenceHolder.IsRepeatOf(details))
        {
            reasons.Add(new(RejectionCode.ShipperReferenceReused, Invariant(
                $"The Shipper reference '{details.ShipperReference}' is already used for Transport Order {referenceHolder.Number}, with different content.")));
        }
    }

    private static void CheckAddresses(TransportOrderDetails details, List<RejectionReason> reasons)
    {
        CheckAddress(details.Collection?.Address, "collection", RejectionCode.CollectionAddressMissing, RejectionCode.CollectionAddressIncomplete, reasons);
        CheckAddress(details.Delivery?.Address, "delivery", RejectionCode.DeliveryAddressMissing, RejectionCode.DeliveryAddressIncomplete, reasons);

        if (IsBlank(details.Delivery?.Consignee?.Name))
        {
            reasons.Add(new(RejectionCode.ConsigneeNameMissing, "The booking has no Consignee name."));
        }
    }

    private static void CheckAddress(Address? address, string which, RejectionCode missing, RejectionCode incomplete, List<RejectionReason> reasons)
    {
        if (address is null)
        {
            reasons.Add(new(missing, $"The booking has no {which} address."));
            return;
        }

        string[] gaps =
        [
            .. IsBlank(address.Street) ? ["street"] : Array.Empty<string>(),
            .. IsBlank(address.Postcode) ? ["postcode"] : Array.Empty<string>(),
            .. IsBlank(address.City) ? ["city"] : Array.Empty<string>(),
            .. IsBlank(address.Country) ? ["country"] : Array.Empty<string>(),
        ];
        if (gaps.Length > 0)
        {
            reasons.Add(new(incomplete, $"The {which} address has no {string.Join(", ", gaps)}."));
        }
    }

    private static void CheckWindows(TransportOrderDetails details, DateTimeOffset now, List<RejectionReason> reasons)
    {
        var collection = details.Collection?.Window;
        if (collection is not { Earliest: { } collectionEarliest, Latest: { } collectionLatest })
        {
            reasons.Add(new(RejectionCode.CollectionWindowMissing, "The booking has no collection window with both an earliest and a latest time."));
        }
        else
        {
            if (collectionEarliest < now)
            {
                reasons.Add(new(RejectionCode.CollectionWindowInPast, "The collection window starts in the past."));
            }

            if (collectionEarliest > collectionLatest)
            {
                reasons.Add(new(RejectionCode.CollectionWindowReversed, "The collection window's earliest time is after its latest time."));
            }
        }

        switch (details.Delivery?.Window)
        {
            case null or { Earliest: null, Latest: null }:
                break;
            case { Earliest: { } earliest, Latest: { } latest }:
                if (earliest > latest)
                {
                    reasons.Add(new(RejectionCode.DeliveryWindowReversed, "The delivery window's earliest time is after its latest time."));
                }

                if (collection?.Earliest is { } collectionStart && latest < collectionStart)
                {
                    reasons.Add(new(RejectionCode.DeliveryWindowBeforeCollection, "The delivery window ends before the collection window starts."));
                }

                break;
            default:
                reasons.Add(new(RejectionCode.DeliveryWindowIncomplete, "The delivery window needs both an earliest and a latest time."));
                break;
        }
    }

    private static void CheckGoodsLines(TransportOrderDetails details, List<RejectionReason> reasons)
    {
        if (details.GoodsLines.Count == 0)
        {
            reasons.Add(new(RejectionCode.GoodsLinesMissing, "The booking has no goods lines."));
            return;
        }

        for (var i = 0; i < details.GoodsLines.Count; i++)
        {
            CheckGoodsLine(details.GoodsLines[i], lineNumber: i + 1, reasons);
        }
    }

    private static void CheckGoodsLine(GoodsLine line, int lineNumber, List<RejectionReason> reasons)
    {
        if (line.PackageType is not { } packageType || !PackageTypes.Contains(packageType, StringComparer.Ordinal))
        {
            var given = IsBlank(line.PackageType) ? "no package type" : $"package type '{line.PackageType}'";
            reasons.Add(new(RejectionCode.PackageTypeInvalid, Invariant(
                $"Goods line {lineNumber} has {given}; it must be one of {string.Join(", ", PackageTypes)}.")));
        }

        if (line.Quantity is not >= 1)
        {
            reasons.Add(new(RejectionCode.QuantityInvalid, Invariant($"Goods line {lineNumber} needs a quantity of at least 1.")));
        }

        if (line.WeightKg is not > 0m)
        {
            reasons.Add(new(RejectionCode.WeightInvalid, Invariant($"Goods line {lineNumber} needs a weight greater than 0 kg.")));
        }

        if (line.DimensionsCm is { } dimensions && new[] { dimensions.Length, dimensions.Width, dimensions.Height }.Any(size => size <= 0m))
        {
            reasons.Add(new(RejectionCode.DimensionsInvalid, Invariant($"Goods line {lineNumber} has dimensions that aren't positive.")));
        }
    }

    private void CheckTotalWeight(TransportOrderDetails details, List<RejectionReason> reasons)
    {
        var totalWeightKg = details.TotalWeightKg();
        if (totalWeightKg > MaxTotalWeightKg)
        {
            reasons.Add(new(RejectionCode.TotalWeightOverLimit, Invariant(
                $"The total weight of {totalWeightKg} kg is over the limit of {MaxTotalWeightKg} kg for a single Transport Order.")));
        }
    }

    private static bool IsBlank([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);
}
