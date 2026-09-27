using Norvia.Tms.Domain.Carriers;

namespace Norvia.Tms.Application.Carriers;

/// <summary>The TMS's read copy of Carrier master data, which the ERP owns (ADR 0002).</summary>
public interface ICarrierDirectory
{
    Carrier? Find(CarrierId id);
}
