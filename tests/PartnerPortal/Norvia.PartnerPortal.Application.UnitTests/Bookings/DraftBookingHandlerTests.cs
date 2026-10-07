using Ardalis.Result;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.Draft;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.Bookings;

public sealed class DraftBookingHandlerTests
{
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly FakeTimeProvider _time = new(Samples.Now);
    private readonly DraftBookingHandler _handler;

    public DraftBookingHandlerTests()
    {
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _handler = new DraftBookingHandler(_personas, _time);
    }

    [Fact]
    public async Task Handle_ShipperWithSavedAddresses_CollectsFromItsOwnAddressAndDeliversToItsConsignee()
    {
        var draft = (await _handler.Handle(new DraftBookingQuery(new PersonaId("brouwer-dranken")), CancellationToken.None)).Value;

        draft.CollectionAddress.Should().Be(new BookingAddress("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NL"));
        draft.DeliveryAddress.Should().Be(new BookingAddress("Oudegracht 112", "3511 AW", "Utrecht", "NL"));
        draft.Consignee.Should().Be(new BookingConsignee("Café De Zwaan", "Sanne de Vries", "+31 30 555 0142"));
    }

    [Fact]
    public async Task Handle_Always_CollectsTomorrowMorning_WithNoDeliveryWindow()
    {
        var draft = (await _handler.Handle(new DraftBookingQuery(new PersonaId("brouwer-dranken")), CancellationToken.None)).Value;

        draft.CollectionWindow.Should().Be(new LocalWindow(new DateTime(2026, 9, 25, 8, 0, 0), new DateTime(2026, 9, 25, 12, 0, 0)));
        draft.DeliveryWindow.Should().Be(new LocalWindow());
    }

    [Fact]
    public async Task Handle_Always_HasOneLineOfPallets()
    {
        var draft = (await _handler.Handle(new DraftBookingQuery(new PersonaId("brouwer-dranken")), CancellationToken.None)).Value;

        draft.GoodsLines.Should().Equal(new BookingGoodsLine(PackageType.Pallet, 2, 850m, new PackageDimensions(120, 80, 150)));
    }

    [Fact]
    public async Task Handle_Always_GivesAReferenceNotUsedBefore()
    {
        var first = (await _handler.Handle(new DraftBookingQuery(new PersonaId("brouwer-dranken")), CancellationToken.None)).Value;
        _time.Advance(TimeSpan.FromSeconds(1));
        var second = (await _handler.Handle(new DraftBookingQuery(new PersonaId("brouwer-dranken")), CancellationToken.None)).Value;

        first.ShipperReference.Should().Be("BK-20260924-081500");
        second.ShipperReference.Should().NotBe(first.ShipperReference);
    }

    [Fact]
    public async Task Handle_PartyWithoutSavedAddresses_LeavesTheAddressesEmpty()
    {
        var draft = (await _handler.Handle(new DraftBookingQuery(new PersonaId("van-dijk-transport")), CancellationToken.None)).Value;

        draft.CollectionAddress.Should().Be(new BookingAddress());
        draft.DeliveryAddress.Should().Be(new BookingAddress());
        draft.Consignee.Should().Be(new BookingConsignee());
    }

    [Fact]
    public async Task Handle_UnknownPersona_IsNotFound()
    {
        var result = await _handler.Handle(new DraftBookingQuery(new PersonaId("nobody")), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }
}
