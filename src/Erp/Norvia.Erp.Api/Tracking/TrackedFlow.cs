namespace Norvia.Erp.Api.Tracking;

/// <summary>Endpoint metadata naming the tracking-contract flow a call belongs to, such as <c>billing</c>.</summary>
public sealed record TrackedFlow(string Name);
