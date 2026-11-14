using Norvia.Erp.Application.MasterData.Shippers.List;

namespace Norvia.Erp.Application.MasterData.Shippers.Save;

public sealed record SaveShipperResult(ShipperView Shipper, MasterDataSaveOutcome Outcome);
