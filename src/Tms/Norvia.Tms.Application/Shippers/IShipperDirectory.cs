using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Application.Shippers;

/// <summary>The TMS's read copy of Shipper master data.</summary>
public interface IShipperDirectory
{
    Shipper? Find(ShipperId id);

    /// <summary>Every Shipper the TMS knows.</summary>
    IReadOnlyList<Shipper> List();
}
