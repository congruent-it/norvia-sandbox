namespace Norvia.Erp.Domain.CarrierCosts;

/// <summary>An Open Carrier cost that an Unknown booking line may be about, with the score the ERP ranked it by (0 to 1).</summary>
public sealed record CandidateCarrierCost(CarrierCost Cost, double Score);
