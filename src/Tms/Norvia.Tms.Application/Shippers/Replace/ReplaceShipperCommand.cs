using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Shippers.Replace;

/// <summary>
/// Replaces the TMS's copy of a Shipper with the ERP's record (E3). Upserts: a new Shipper appears. Idempotent on the Master-data revision:
/// a revision that is not higher than the copy's changes nothing.
/// </summary>
public sealed record ReplaceShipperCommand(string Id, string Name, bool Active, int Revision) : ICommand<Result<ReplaceShipperResult>>;

/// <param name="Applied">False when the copy already had this revision or a later one.</param>
/// <param name="Revision">The revision the copy holds now.</param>
public sealed record ReplaceShipperResult(bool Applied, int Revision);
