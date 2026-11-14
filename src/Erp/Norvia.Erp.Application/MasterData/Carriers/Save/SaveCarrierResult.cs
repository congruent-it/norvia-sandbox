using Norvia.Erp.Application.MasterData.Carriers.List;

namespace Norvia.Erp.Application.MasterData.Carriers.Save;

public sealed record SaveCarrierResult(CarrierView Carrier, MasterDataSaveOutcome Outcome);
