namespace Norvia.Tms.Api.Fleet;

/// <summary>The body of <c>PUT /v1/drivers/{id}</c>: the ERP's Driver whole, with its Master-data revision. <c>Licences</c> are B, C or CE.</summary>
public sealed record ReplaceDriverRequest(string Name, IReadOnlyList<string> Licences, bool Active, int Revision);
