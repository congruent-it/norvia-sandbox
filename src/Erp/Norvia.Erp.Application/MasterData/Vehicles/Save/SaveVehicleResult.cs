using Norvia.Erp.Application.MasterData.Vehicles;

namespace Norvia.Erp.Application.MasterData.Vehicles.Save;

public sealed record SaveVehicleResult(VehicleView Vehicle, MasterDataSaveOutcome Outcome);
