using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.MasterData.Shippers.List;

namespace Norvia.Erp.Application.MasterData.Shippers.Save;

/// <summary>
/// Finance creates or edits a Shipper (E3). Idempotent on content: a repeat of what the Shipper already is changes and publishes nothing.
/// </summary>
public sealed record SaveShipperCommand(string Id, string Name, bool Active) : ICommand<Result<SaveShipperResult>>;

public sealed record SaveShipperResult(ShipperView Shipper, MasterDataSaveOutcome Outcome);
