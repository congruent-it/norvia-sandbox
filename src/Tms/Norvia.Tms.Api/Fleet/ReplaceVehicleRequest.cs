namespace Norvia.Tms.Api.Fleet;

/// <summary>The body of <c>PUT /v1/vehicles/{id}</c>: the ERP's Vehicle whole, with its Master-data revision. <c>Type</c> is Van, RigidTruck or TractorWithTrailer.</summary>
public sealed record ReplaceVehicleRequest(string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active, int Revision);
