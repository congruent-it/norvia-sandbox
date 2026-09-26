using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.Runs.Open;

/// <summary>A Logic App run as it is now, fetched live: its steps with their status, duration and error, not their bodies.</summary>
public sealed record OpenRunQuery(string LogicApp, string Workflow, string RunId) : IQuery<Result<RunDetail>>;
