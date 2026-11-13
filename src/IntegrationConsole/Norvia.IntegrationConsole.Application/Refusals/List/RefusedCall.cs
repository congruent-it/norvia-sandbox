namespace Norvia.IntegrationConsole.Application.Refusals.List;

/// <summary>One refused call, view only.</summary>
public sealed record RefusedCall(DateTimeOffset Time, string Operation, int StatusCode, string FailureCode, string Caller, string? CorrelationId);
