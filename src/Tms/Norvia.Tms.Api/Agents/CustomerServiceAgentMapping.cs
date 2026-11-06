using Norvia.Tms.Application.CanonicalModel;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.Agents;

/// <summary>Maps a Rejected booking to the agent view, field by field: a field the view doesn't name never reaches an Agent.</summary>
internal static class CustomerServiceAgentMapping
{
    public static AgentRejectedBookingView Of(TransportOrderView booking)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var order = booking.TransportOrder;
        return new AgentRejectedBookingView(
            booking.Id,
            booking.Status.ToString(),
            booking.ReceivedAt,
            booking.CorrelationId,
            booking.Channel.ToString(),
            new AgentShipperView(booking.Shipper.Id, booking.Shipper.Name),
            [.. booking.RejectionReasons.Select(reason => new AgentRejectionReasonView(reason.Code, reason.Message, FieldOf(reason.Code)))],
            new AgentSubmittedBookingView(
                order.ShipperReference,
                order.Collection is { } collection
                    ? new AgentCollectionView(collection.Address?.Postcode, collection.Address?.City, collection.Address?.Country, collection.Window?.Earliest, collection.Window?.Latest)
                    : null,
                order.Delivery is { } delivery ? new AgentDeliveryView(delivery.Window?.Earliest, delivery.Window?.Latest) : null,
                [.. (order.GoodsLines ?? []).Select(line => new AgentGoodsLineView(
                    line.PackageType,
                    line.Quantity,
                    line.WeightKg,
                    line.DimensionsCm is { } dimensions ? new AgentDimensionsView(dimensions.Length, dimensions.Width, dimensions.Height) : null))]));
    }

    /// <summary>The submitted field a rejection code is about, in the Canonical Model's names.</summary>
    public static string FieldOf(string code) => Enum.TryParse<RejectionCode>(code, out var parsed)
        ? parsed switch
        {
            RejectionCode.UnknownShipper or RejectionCode.InactiveShipper => "shipperId",
            RejectionCode.ShipperReferenceMissing or RejectionCode.ShipperReferenceReused or RejectionCode.ShipperReferenceTooLong => "shipperReference",
            RejectionCode.CollectionAddressMissing or RejectionCode.CollectionAddressIncomplete => "collection.address",
            RejectionCode.DeliveryAddressMissing or RejectionCode.DeliveryAddressIncomplete => "delivery.address",
            RejectionCode.ConsigneeNameMissing => "delivery.consignee",
            RejectionCode.CollectionWindowMissing or RejectionCode.CollectionWindowInPast or RejectionCode.CollectionWindowReversed => "collection.window",
            RejectionCode.DeliveryWindowIncomplete or RejectionCode.DeliveryWindowReversed => "delivery.window",
            RejectionCode.DeliveryWindowBeforeCollection => "delivery.window",
            RejectionCode.GoodsLinesMissing => "goodsLines",
            RejectionCode.PackageTypeInvalid => "goodsLines[].packageType",
            RejectionCode.QuantityInvalid => "goodsLines[].quantity",
            RejectionCode.WeightInvalid => "goodsLines[].weightKg",
            RejectionCode.DimensionsInvalid => "goodsLines[].dimensionsCm",
            RejectionCode.TotalWeightOverLimit => "goodsLines[].weightKg",
            _ => "unknown",
        }
        : "unknown";
}
