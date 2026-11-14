using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Fleet.ReplaceVehicle;

/// <summary>
/// Replaces the TMS's copy of a Vehicle with the ERP's record (E3). Upserts: a new Vehicle appears. Idempotent on the Master-data revision:
/// a revision that is not higher than the copy's changes nothing. It touches no Roster, Off the road day or Trip.
/// </summary>
/// <param name="Type">The Vehicle type by name: Van, RigidTruck or TractorWithTrailer.</param>
public sealed record ReplaceVehicleCommand(string Id, string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active, int Revision)
    : ICommand<Result<ReplaceVehicleResult>>;

/// <param name="Applied">False when the copy already had this revision or a later one.</param>
/// <param name="Revision">The revision the copy holds now.</param>
public sealed record ReplaceVehicleResult(bool Applied, int Revision);
