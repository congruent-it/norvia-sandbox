namespace Norvia.Erp.Api.MasterData;

/// <summary>The body of <c>PUT /v1/shippers/{id}</c>: the whole Shipper but for its ID, which the route carries.</summary>
public sealed record SaveShipperRequest(string Name, bool Active);
