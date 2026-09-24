namespace Norvia.Tms.Api.Tracking;

/// <summary>Endpoint metadata naming the tracking-contract flow a call belongs to, such as <c>transport-order-intake</c>.</summary>
public sealed record TrackedFlow(string Name);
