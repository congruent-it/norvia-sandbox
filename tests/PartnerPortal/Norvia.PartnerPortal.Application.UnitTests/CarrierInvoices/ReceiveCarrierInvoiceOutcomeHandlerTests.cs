using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.CarrierInvoices.Outcome;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierInvoices;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

public sealed class ReceiveCarrierInvoiceOutcomeHandlerTests
{
    private readonly IRepository<CarrierInvoiceOutcome> _repository = Substitute.For<IRepository<CarrierInvoiceOutcome>>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly ReceiveCarrierInvoiceOutcomeHandler _handler;

    public ReceiveCarrierInvoiceOutcomeHandlerTests()
    {
        _personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        _handler = new ReceiveCarrierInvoiceOutcomeHandler(_repository, _personas, new FakeTimeProvider(Samples.Now));
    }

    private static ReceiveCarrierInvoiceOutcomeCommand Command(string partyId = "CAR-0001") => new(
        partyId, "corr-2", "INV-1", 190m,
        [new("VDT-1", CarrierInvoiceOutcomeResult.Matched, 100m, null), new("VDT-2", CarrierInvoiceOutcomeResult.AcceptedAdjusted, 90m, "Waiting time")]);

    [Fact]
    public async Task Handle_ANewOutcome_IsStoredForTheCarriersPersona()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierInvoiceOutcomeResult("INV-1", Repeated: false));
        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierInvoiceOutcome>(outcome =>
                outcome.PersonaId == Samples.VanDijkTransport.Id
                && outcome.InvoiceNumber == "INV-1"
                && outcome.CorrelationId == "corr-2"
                && outcome.ReceivedAt == Samples.Now
                && outcome.ApprovedAmount == 190m
                && outcome.Lines.Count == 2
                && outcome.Lines[1].Remark == "Waiting time"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheOutcomeOfTheSameInvoiceNumberAgain_IsAcknowledged_WithoutBeingStoredTwice()
    {
        _repository.AnyAsync(Arg.Any<CarrierInvoiceOutcomeByNumberSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Should().Be(new ReceiveCarrierInvoiceOutcomeResult("INV-1", Repeated: true));
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierInvoiceOutcome>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CAR-9999")]
    [InlineData("SHP-0001")]
    public async Task Handle_ForNoCarrierPersona_IsNotFound(string partyId)
    {
        var result = await _handler.Handle(Command(partyId), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierInvoiceOutcome>(), Arg.Any<CancellationToken>());
    }
}
