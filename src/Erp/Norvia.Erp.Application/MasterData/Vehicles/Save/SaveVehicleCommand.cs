using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.Vehicles.Save;

/// <summary>
/// Finance creates or edits a Vehicle (E3). Idempotent on content: a repeat of what the Vehicle already is changes and publishes nothing.
/// </summary>
/// <param name="Type">The Vehicle type by name: Van, RigidTruck or TractorWithTrailer.</param>
public sealed record SaveVehicleCommand(string Id, string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active)
    : ICommand<Result<SaveVehicleResult>>;
