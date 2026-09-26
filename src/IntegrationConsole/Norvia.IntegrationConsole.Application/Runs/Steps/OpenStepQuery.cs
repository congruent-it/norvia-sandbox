using Ardalis.Result;
using Mediator;

namespace Norvia.IntegrationConsole.Application.Runs.Steps;

/// <summary>
/// One step of a Logic App run with its input and output bodies in full, fetched live when the operator opens it. Bodies
/// hold personal data, so they are fetched only here, and never copied anywhere, the console's logs included.
/// </summary>
public sealed record OpenStepQuery(string LogicApp, string Workflow, string RunId, string Step) : IQuery<Result<StepDetail>>;
