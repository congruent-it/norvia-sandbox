using Norvia.Tms.Application.Carriers;
using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Application.Shipments;

/// <param name="Name">Null when the Carrier isn't in the TMS's Carrier data.</param>
public sealed record CarrierView(string Id, string? Name)
{
    public static CarrierView Of(CarrierId id, ICarrierDirectory carriers)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(carriers);
        return new(id.Value, carriers.Find(id)?.Name);
    }
}
