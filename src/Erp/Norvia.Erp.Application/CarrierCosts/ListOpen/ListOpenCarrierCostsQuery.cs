using Mediator;

namespace Norvia.Erp.Application.CarrierCosts.ListOpen;

/// <summary>The Carriers with Open Carrier costs, by Carrier name, each with its Open costs, the earliest delivery first.</summary>
public sealed record ListOpenCarrierCostsQuery : IQuery<IReadOnlyList<OpenCarrierCostsOfCarrierView>>;

public sealed record OpenCarrierCostsOfCarrierView(string CarrierId, string CarrierName, decimal Total, IReadOnlyList<CarrierCostView> Costs);
