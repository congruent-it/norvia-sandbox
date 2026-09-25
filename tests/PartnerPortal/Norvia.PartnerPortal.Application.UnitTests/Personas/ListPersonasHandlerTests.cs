using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Application.Personas.List;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.Personas;

public sealed class ListPersonasHandlerTests
{
    [Fact]
    public async Task Handle_ListsEveryPersona_InTheSeedFilesOrder()
    {
        var personas = Substitute.For<IPersonaDirectory>();
        personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);

        var listed = await new ListPersonasHandler(personas).Handle(new ListPersonasQuery(), CancellationToken.None);

        listed.Should().Equal(
            new PersonaView("brouwer-dranken", "Brouwer Dranken", PartyType.Shipper),
            new PersonaView("van-dijk-transport", "Van Dijk Transport", PartyType.Carrier));
    }
}
