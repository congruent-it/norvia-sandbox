namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>A Logic App run a hop was: the Logic App Standard app, its workflow, and the run's ID.</summary>
public sealed record WorkflowRun(string LogicApp, string Workflow, string RunId);
