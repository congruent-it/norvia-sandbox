namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>The error a step ended with, as Logic Apps reports it.</summary>
public sealed record StepError(string Code, string? Message);
