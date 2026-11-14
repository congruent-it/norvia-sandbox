using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Fleet.ReplaceDriver;

/// <summary>
/// Replaces the TMS's copy of a Driver with the ERP's record (E3). Upserts: a new Driver appears. Idempotent on the Master-data revision:
/// a revision that is not higher than the copy's changes nothing. It touches no Roster, Absence or Trip.
/// </summary>
/// <param name="Licences">The licence categories held by name: B, C or CE.</param>
public sealed record ReplaceDriverCommand(string Id, string Name, IReadOnlyList<string> Licences, bool Active, int Revision)
    : ICommand<Result<ReplaceDriverResult>>;

/// <param name="Applied">False when the copy already had this revision or a later one.</param>
/// <param name="Revision">The revision the copy holds now.</param>
public sealed record ReplaceDriverResult(bool Applied, int Revision);
