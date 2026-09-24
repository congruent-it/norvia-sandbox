namespace Norvia.Tms.Domain.TransportOrders;

/// <summary>
/// Why a booking was rejected. The names are stable codes that callers rely on: never rename one, only add.
/// </summary>
public enum RejectionCode
{
    UnknownShipper,
    InactiveShipper,
    ShipperReferenceMissing,
    CollectionAddressMissing,
    CollectionAddressIncomplete,
    DeliveryAddressMissing,
    DeliveryAddressIncomplete,
    ConsigneeNameMissing,
    CollectionWindowMissing,
    CollectionWindowInPast,
    CollectionWindowReversed,
    DeliveryWindowIncomplete,
    DeliveryWindowReversed,
    DeliveryWindowBeforeCollection,
    GoodsLinesMissing,
    PackageTypeInvalid,
    QuantityInvalid,
    WeightInvalid,
    DimensionsInvalid,
    TotalWeightOverLimit,
}
