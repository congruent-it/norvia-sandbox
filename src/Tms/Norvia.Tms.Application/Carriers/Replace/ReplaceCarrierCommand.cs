using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Carriers.Replace;

/// <summary>
/// Replaces the TMS's copy of a Carrier with the ERP's record (E3). Upserts: a new Carrier appears. Idempotent on the Master-data revision:
/// a revision that is not higher than the copy's changes nothing.
/// </summary>
public sealed record ReplaceCarrierCommand(string Id, string Name, bool Active, int Revision) : ICommand<Result<ReplaceCarrierResult>>;
