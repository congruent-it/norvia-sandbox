using Norvia.IntegrationConsole.Application.Trails;

namespace Norvia.IntegrationConsole.Application.Runs.Steps;

/// <summary>A step of a Logic App run, opened with its bodies.</summary>
/// <param name="IsKept">Whether run history still keeps the run and the step. Once it doesn't, both bodies are none.</param>
/// <param name="RunHistoryDays">How long run history keeps a run.</param>
public sealed record StepDetail(WorkflowRun Run, string Step, bool IsKept, StepBody Input, StepBody Output, int RunHistoryDays);
