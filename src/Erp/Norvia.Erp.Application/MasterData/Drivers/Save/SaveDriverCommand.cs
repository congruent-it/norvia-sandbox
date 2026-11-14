using Ardalis.Result;
using Mediator;

namespace Norvia.Erp.Application.MasterData.Drivers.Save;

/// <summary>
/// Finance creates or edits a Driver (E3). Idempotent on content: a repeat of what the Driver already is, the licences in any order,
/// changes and publishes nothing.
/// </summary>
/// <param name="Licences">The licence categories held by name: B, C or CE.</param>
public sealed record SaveDriverCommand(string Id, string Name, IReadOnlyList<string> Licences, bool Active) : ICommand<Result<SaveDriverResult>>;
