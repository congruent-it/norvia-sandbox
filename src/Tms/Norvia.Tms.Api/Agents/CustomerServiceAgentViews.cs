namespace Norvia.Tms.Api.Agents;

// The Customer service agent's view is a narrower read than the full read models (ADR 0040): statuses, codes, times, numbers and the
// Shipper's business name. Every field here is on an allowlist, so a field added to a read model stays out of the view until someone
// adds it here. No Consignee name, address or contact, no delivery address, no free text the Shipper typed and no file's contents:
// AgentViewsTests holds the list.

/// <summary>A Rejected booking as the Customer service agent reads it (<c>get_rejected_booking</c>).</summary>
/// <param name="Shipper">The Shipper's ID and business name.</param>
/// <param name="RejectionReasons">Every rule the booking broke.</param>
/// <param name="Submitted">The content the Shipper submitted, without the Consignee's.</param>
public sealed record AgentRejectedBookingView(
    Guid Id,
    string Status,
    DateTimeOffset ReceivedAt,
    string CorrelationId,
    string Channel,
    AgentShipperView Shipper,
    IReadOnlyList<AgentRejectionReasonView> RejectionReasons,
    AgentSubmittedBookingView Submitted);

/// <param name="Name">The Shipper's business name, or null when the TMS doesn't know the Shipper.</param>
public sealed record AgentShipperView(string Id, string? Name);

/// <param name="Code">The stable rejection code, such as <c>WeightInvalid</c>.</param>
/// <param name="Message">The plain-language message the rule gave.</param>
/// <param name="Field">The submitted field that failed, such as <c>goodsLines[].weightKg</c>.</param>
public sealed record AgentRejectionReasonView(string Code, string Message, string Field);

/// <summary>What the Shipper submitted. The delivery has its window only: its address and Consignee are not the agent's to read.</summary>
public sealed record AgentSubmittedBookingView(
    string? ShipperReference,
    AgentCollectionView? Collection,
    AgentDeliveryView? Delivery,
    IReadOnlyList<AgentGoodsLineView> GoodsLines);

/// <summary>The collection: the Shipper's own site, without its street.</summary>
public sealed record AgentCollectionView(string? Postcode, string? City, string? Country, DateTimeOffset? Earliest, DateTimeOffset? Latest);

public sealed record AgentDeliveryView(DateTimeOffset? Earliest, DateTimeOffset? Latest);

public sealed record AgentGoodsLineView(string? PackageType, int? Quantity, decimal? WeightKg, AgentDimensionsView? DimensionsCm);

public sealed record AgentDimensionsView(decimal? Length, decimal? Width, decimal? Height);
