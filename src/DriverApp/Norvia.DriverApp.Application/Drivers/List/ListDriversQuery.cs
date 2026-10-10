using Mediator;

namespace Norvia.DriverApp.Application.Drivers.List;

public sealed record ListDriversQuery : IQuery<IReadOnlyList<DriverView>>;
