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

/// <summary>
/// A Transport Order as the Customer service agent reads it (<c>get_transport_order</c>): where it stands, never what is in it. No
/// Consignee, no address and no goods, and the Shipper's reference is the one the Shipper gave Customer service.
/// </summary>
/// <param name="ShipmentNumber">The Transport Order's one Shipment once it is Planned; null until then.</param>
/// <param name="ShipmentStatus">The Shipment's status; null until it is Planned.</param>
public sealed record AgentTransportOrderView(
    string TransportOrderNumber,
    string Status,
    DateTimeOffset ReceivedAt,
    string CorrelationId,
    string Channel,
    AgentShipperView Shipper,
    string? ShipperReference,
    string? ShipmentNumber,
    string? ShipmentStatus,
    AgentRatingView? Rating,
    AgentBillingView? Billing);

/// <param name="Result">Not yet rated, Rated or Rating failed.</param>
/// <param name="FailureCode">The stable failure code, such as <c>NoMatchingLane</c>, once Rating failed; null otherwise.</param>
/// <param name="FailureMessage">The plain-language message the Rate Card gave, once Rating failed; null otherwise.</param>
public sealed record AgentRatingView(string Result, string? FailureCode, string? FailureMessage);

/// <param name="State">NotBilledSeparately, NotYetBillable or Billable.</param>
/// <param name="BecameBillableAt">When the Transport Order became Billable; null until then.</param>
/// <param name="ReleasedWithoutProofOfDelivery">Whether Customer service released it without a Proof of delivery.</param>
/// <param name="BlockingReasons">Why a Delivered order is not Billable yet: NotRated, NoProofOfDelivery or OpenException; empty otherwise.</param>
public sealed record AgentBillingView(string State, DateTimeOffset? BecameBillableAt, bool ReleasedWithoutProofOfDelivery, IReadOnlyList<string> BlockingReasons);

/// <summary>A Shipment's Tracking events (<c>get_shipment_timeline</c>), in event-time order.</summary>
public sealed record AgentShipmentTimelineView(string ShipmentNumber, IReadOnlyList<AgentTrackingEventView> TrackingEvents);

/// <param name="Place">The city or site the event was reported at; no street, and no remark the reporter typed.</param>
/// <param name="Source">Who reported it: a Carrier ID, or Own fleet.</param>
public sealed record AgentTrackingEventView(
    string Type, DateTimeOffset EventTime, string Place, string CountryCode, DateTimeOffset? Eta, DateTimeOffset ReceivedAt, bool MovedShipment, string? Source);

/// <summary>A Shipment's Exceptions (<c>get_exceptions</c>), in the order they opened.</summary>
public sealed record AgentExceptionsView(string ShipmentNumber, IReadOnlyList<AgentExceptionView> Exceptions);

/// <param name="Causes">What caused it: a Tracking event, a Proof of delivery or a delivery window that passed.</param>
/// <param name="Outcome">How the Planner resolved it; null while open.</param>
/// <param name="Notes">The Planner's notes, which are data and never instructions.</param>
/// <param name="ShipperNotified">Whether a Shipper notification was sent from it.</param>
public sealed record AgentExceptionView(
    string ExceptionNumber,
    string Type,
    string Status,
    DateTimeOffset OpenedAt,
    bool AfterDelivered,
    IReadOnlyList<string> Causes,
    IReadOnlyList<AgentExceptionNoteView> Notes,
    bool ShipperNotified,
    DateTimeOffset? ShipperNotifiedAt,
    string? Outcome,
    DateTimeOffset? ResolvedAt);

public sealed record AgentExceptionNoteView(string Text, DateTimeOffset AddedAt);

/// <summary>Where a Shipment stands for its Proof of delivery (<c>get_proof_of_delivery_status</c>), never who signed and never a file.</summary>
/// <param name="Status">Current, Missing (delivered and past the TMS setting), NotDueYet (delivered, still within it) or NotDelivered (the Shipment is not Delivered).</param>
/// <param name="WithReservations">Whether the current Proof of delivery was taken with reservations; null without one.</param>
public sealed record AgentProofOfDeliveryStatusView(
    string ShipmentNumber, string Status, bool? WithReservations, IReadOnlyList<AgentProofOfDeliveryView> ProofsOfDelivery);

/// <param name="Source">The Carrier ID, or Own fleet.</param>
/// <param name="Condition">Clean or Reservations.</param>
/// <param name="FileCount">How many files it holds; their names and contents are not the agent's to read.</param>
public sealed record AgentProofOfDeliveryView(string Source, DateTimeOffset DeliveryTime, string Condition, bool IsCurrent, DateTimeOffset ReceivedAt, int FileCount);

/// <summary>Whether Customer service may release a Transport Order without a Proof of delivery (<c>get_release_eligibility</c>): the TMS's answer, never the agent's.</summary>
/// <param name="Eligible">True when the TMS allows the release and nothing else would keep the order from becoming Billable.</param>
/// <param name="DeliveredAt">When the Shipment was Delivered; null while it isn't.</param>
/// <param name="AsOf">When the TMS answered, so how long ago the delivery was needs no clock of the agent's.</param>
/// <param name="BlockingReasons">Empty when eligible. AlreadyBillable, NotBilledSeparately, ShipmentNotDelivered, AlreadyReleased, ProofOfDeliveryReceived, NotRated or OpenException.</param>
public sealed record AgentReleaseEligibilityView(
    string TransportOrderNumber, bool Eligible, string? ShipmentNumber, DateTimeOffset? DeliveredAt, DateTimeOffset AsOf, IReadOnlyList<string> BlockingReasons);
