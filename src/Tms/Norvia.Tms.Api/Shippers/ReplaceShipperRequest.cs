namespace Norvia.Tms.Api.Shippers;

/// <summary>The body of <c>PUT /v1/shippers/{id}</c>: the ERP's Shipper whole, with its Master-data revision.</summary>
public sealed record ReplaceShipperRequest(string Name, bool Active, int Revision);
