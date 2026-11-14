namespace Norvia.Erp.Api.MasterData;

/// <summary>The body of <c>PUT /v1/carriers/{id}</c>: the whole Carrier but for its ID, which the route carries.</summary>
public sealed record SaveCarrierRequest(string Name, bool Active);
