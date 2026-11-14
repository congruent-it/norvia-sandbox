using Mediator;

namespace Norvia.Erp.Application.MasterData.Drivers.List;

/// <summary>The Own fleet's Drivers in the ERP's master data.</summary>
public sealed record ListDriversQuery : IQuery<IReadOnlyList<DriverView>>;
