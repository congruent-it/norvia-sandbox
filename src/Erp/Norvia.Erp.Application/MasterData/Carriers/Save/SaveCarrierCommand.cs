using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.Carriers.Save;

/// <summary>
/// Finance creates or edits a Carrier (E3). Idempotent on content, and on the sender's key when it sends one: a repeat of what the Carrier already is, or of a save already applied under the same key, changes and publishes nothing.
/// </summary>
public sealed record SaveCarrierCommand(string Id, string Name, bool Active, string? IdempotencyKey = null) : ICommand<Result<SaveCarrierResult>>;
