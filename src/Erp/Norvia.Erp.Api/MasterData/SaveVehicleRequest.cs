namespace Norvia.Erp.Api.MasterData;

/// <summary>The body of <c>PUT /v1/vehicles/{id}</c>: the whole Vehicle but for its ID, which the route carries. <c>Type</c> is Van, RigidTruck or TractorWithTrailer.</summary>
public sealed record SaveVehicleRequest(string Registration, string Type, int PayloadKg, int PalletPlaces, bool Active);
