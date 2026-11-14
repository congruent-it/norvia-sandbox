using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.Carriers.Save;

/// <summary>
/// Finance creates or edits a Carrier (E3). Idempotent on content: a repeat of what the Carrier already is changes and publishes nothing.
/// </summary>
public sealed record SaveCarrierCommand(string Id, string Name, bool Active) : ICommand<Result<SaveCarrierResult>>;
