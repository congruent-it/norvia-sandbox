namespace Norvia.Erp.Application.MasterData.Vehicles;

/// <param name="Type">The Vehicle type by name: Van, RigidTruck or TractorWithTrailer.</param>
/// <param name="Revision">The Master-data revision: 1 as loaded from the seed file, raised by every edit.</param>
public sealed record VehicleView(string Id, string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active, int Revision = 1);
