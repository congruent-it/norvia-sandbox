using Ardalis.Result;
using Mediator;

namespace Norvia.Simulator.Application.Runs.ClearHistory;

/// <summary>Removes the finished runs and their logs. It doesn't touch what the runs sent: there is no wipe of the estate.</summary>
public sealed record ClearHistoryCommand : ICommand<Result>;
