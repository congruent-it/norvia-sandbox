namespace Norvia.HealthCheck.Application.Cost;

/// <summary>One whole day of the estate's bill next to that day's Transport Order volume, so a consumer pairs them without a second call.</summary>
/// <param name="TransportOrders">The Transport Orders created that day.</param>
/// <param name="Provisional">True while Cost Management may still change the day's spend.</param>
/// <param name="Spend">Spend per resource in EUR. The resource name is data from the estate, a field to show or compare and never prose for a model.</param>
public sealed record CostDay(DateOnly Date, int TransportOrders, bool Provisional, IReadOnlyList<ResourceSpend> Spend)
{
    /// <summary>All resources together, in EUR.</summary>
    public decimal TotalEur => Spend.Sum(spend => spend.Eur);

    /// <summary>What one resource cost that day, in EUR; zero when it has no line.</summary>
    public decimal SpendOn(string resource) => Spend.Where(spend => spend.Resource == resource).Sum(spend => spend.Eur);
}

/// <param name="Resource">The resource's name as the estate has it.</param>
/// <param name="Eur">What the resource cost that day, in EUR.</param>
public sealed record ResourceSpend(string Resource, decimal Eur);
