using Norvia.Erp.Application.MasterData.RateCards;

namespace Norvia.Erp.Web.ErpApi;

/// <summary>What Finance's master-data forms send to the ERP API (ADR 0028's split: the ERP UI reads in process and changes things through the API).</summary>
public interface IMasterDataCommands
{
    Task<MasterDataAnswer> SaveShipperAsync(string id, string name, bool active, CancellationToken cancellationToken);

    Task<MasterDataAnswer> SaveCarrierAsync(string id, string name, bool active, CancellationToken cancellationToken);

    Task<MasterDataAnswer> SaveVehicleAsync(string id, string registration, string type, int payloadKg, int palletPlaces, bool active, CancellationToken cancellationToken);

    Task<MasterDataAnswer> SaveDriverAsync(string id, string name, IReadOnlyList<string> licences, bool active, CancellationToken cancellationToken);

    Task<MasterDataAnswer> SaveRateCardAsync(string id, string shipperId, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken);

    Task<MasterDataAnswer> SaveCarrierRateCardAsync(string id, string carrierId, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken);
}
