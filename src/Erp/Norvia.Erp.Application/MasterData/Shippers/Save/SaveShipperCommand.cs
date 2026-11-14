using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.Shippers.Save;

/// <summary>
/// Finance creates or edits a Shipper (E3). Idempotent on content, and on the sender's key when it sends one: a repeat of what the Shipper already is, or of a save already applied under the same key, changes and publishes nothing.
/// </summary>
public sealed record SaveShipperCommand(string Id, string Name, bool Active, string? IdempotencyKey = null) : ICommand<Result<SaveShipperResult>>;
