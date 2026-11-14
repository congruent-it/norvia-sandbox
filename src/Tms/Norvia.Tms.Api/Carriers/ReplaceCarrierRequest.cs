namespace Norvia.Tms.Api.Carriers;

/// <summary>The body of <c>PUT /v1/carriers/{id}</c>: the ERP's Carrier whole, with its Master-data revision.</summary>
public sealed record ReplaceCarrierRequest(string Name, bool Active, int Revision);
