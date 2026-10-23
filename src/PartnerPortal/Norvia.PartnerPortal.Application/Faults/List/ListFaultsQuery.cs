using Mediator;

namespace Norvia.PartnerPortal.Application.Faults.List;

public sealed record ListFaultsQuery : IQuery<IReadOnlyList<FaultView>>;
