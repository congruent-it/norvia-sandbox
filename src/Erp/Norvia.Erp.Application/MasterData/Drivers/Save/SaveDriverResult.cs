using Norvia.Erp.Application.MasterData.Drivers;

namespace Norvia.Erp.Application.MasterData.Drivers.Save;

public sealed record SaveDriverResult(DriverView Driver, MasterDataSaveOutcome Outcome);
