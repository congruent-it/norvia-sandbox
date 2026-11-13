namespace Norvia.IntegrationConsole.Application.Refusals.List;

/// <summary>How many calls one caller had refused with one status.</summary>
public sealed record RefusalGroup(string Caller, int StatusCode, int Count);
