using Norvia.Tms.Domain.Shipments;

namespace Norvia.Tms.Application.Exceptions.Timeline;

/// <summary>A delivery window the Planner agreed for a Shipment, on its timeline (P7). The last one counts and earlier ones stay.</summary>
public sealed record AgreedDeliveryWindowView(DateTimeOffset Earliest, DateTimeOffset Latest, string ExceptionNumber, DateTimeOffset AgreedAt)
{
    public static AgreedDeliveryWindowView Of(AgreedDeliveryWindow agreed)
    {
        ArgumentNullException.ThrowIfNull(agreed);
        return new(agreed.Earliest, agreed.Latest, agreed.ExceptionNumber.Value, agreed.AgreedAt);
    }
}
