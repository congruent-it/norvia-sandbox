using Mediator;

namespace Norvia.PartnerPortal.Application.Personas.List;

public sealed class ListPersonasHandler(IPersonaDirectory personas) : IQueryHandler<ListPersonasQuery, IReadOnlyList<PersonaView>>
{
    public ValueTask<IReadOnlyList<PersonaView>> Handle(ListPersonasQuery query, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<PersonaView>>([.. personas.All.Select(PersonaView.From)]);
}
