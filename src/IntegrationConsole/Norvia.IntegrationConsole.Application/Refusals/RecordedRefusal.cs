namespace Norvia.IntegrationConsole.Application.Refusals;

/// <summary>A call API Management refused at the edge, with what it tracked (the tracking contract).</summary>
/// <param name="Operation">The API operation, such as <c>Submit Transport Order</c>.</param>
/// <param name="StatusCode">401, 403 or 429.</param>
/// <param name="FailureCode">The policy's <c>failureCode</c>, or <c>SubscriptionKeyRejected</c> for the gateway's own 401.</param>
/// <param name="Caller">The token's <c>azp</c>, or the subscription's name where no token was read; null when neither is known.</param>
/// <param name="CorrelationId">Opens the Message trail, or null when none was recorded.</param>
public sealed record RecordedRefusal(DateTimeOffset Time, string Operation, int StatusCode, string FailureCode, string? Caller, string? CorrelationId);
