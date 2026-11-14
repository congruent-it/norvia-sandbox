namespace Norvia.Erp.Api.MasterData;

/// <summary>The body of <c>PUT /v1/drivers/{id}</c>: the whole Driver but for its ID, which the route carries. <c>Licences</c> are B, C or CE.</summary>
public sealed record SaveDriverRequest(string Name, IReadOnlyList<string> Licences, bool Active);
