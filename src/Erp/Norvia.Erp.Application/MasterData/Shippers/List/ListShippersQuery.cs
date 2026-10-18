using Mediator;

namespace Norvia.Erp.Application.MasterData.Shippers.List;

/// <summary>The Shippers in the ERP's master data, by name.</summary>
public sealed record ListShippersQuery : IQuery<IReadOnlyList<ShipperView>>;
