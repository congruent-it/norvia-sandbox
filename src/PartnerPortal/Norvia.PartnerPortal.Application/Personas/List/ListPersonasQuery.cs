using Mediator;

namespace Norvia.PartnerPortal.Application.Personas.List;

public sealed record ListPersonasQuery : IQuery<IReadOnlyList<PersonaView>>;
