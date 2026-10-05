using Mediator;

namespace Norvia.Tms.Application.ProofsOfDelivery.ListMissing;

public sealed record ListDeliveredShipmentsWithoutProofOfDeliveryQuery : IQuery<IReadOnlyList<DeliveredShipmentWithoutProofOfDeliverySummary>>;
