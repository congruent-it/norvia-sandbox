using Mediator;

namespace Norvia.Erp.Application.MasterData.Carriers.List;

/// <summary>The Carriers in the ERP's master data, by name.</summary>
public sealed record ListCarriersQuery : IQuery<IReadOnlyList<CarrierView>>;
