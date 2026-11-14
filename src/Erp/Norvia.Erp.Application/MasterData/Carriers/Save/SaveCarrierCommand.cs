using Ardalis.Result;
using Mediator;
using Norvia.Erp.Application.MasterData.Carriers.List;

namespace Norvia.Erp.Application.MasterData.Carriers.Save;

/// <summary>
/// Finance creates or edits a Carrier (E3). Idempotent on content: a repeat of what the Carrier already is changes and publishes nothing.
/// </summary>
public sealed record SaveCarrierCommand(string Id, string Name, bool Active) : ICommand<Result<SaveCarrierResult>>;

public sealed record SaveCarrierResult(CarrierView Carrier, MasterDataSaveOutcome Outcome);
