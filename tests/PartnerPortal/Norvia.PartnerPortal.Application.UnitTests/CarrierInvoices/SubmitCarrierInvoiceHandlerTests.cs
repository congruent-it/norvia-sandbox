using Ardalis.Result;
using Ardalis.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.CarrierInvoices;

public sealed class SubmitCarrierInvoiceHandlerTests
{
    private static readonly CarrierInvoiceReceipt Receipt = new(
        Repeated: false, CarrierInvoiceStatus.Received, ApprovedAmount: null,
        [new("VDT-1", 100m, CarrierInvoiceLineResult.Matched), new("VDT-2", 90m, CarrierInvoiceLineResult.PriceMismatch)]);

    private readonly IRepository<CarrierInvoice> _repository = Substitute.For<IRepository<CarrierInvoice>>();
    private readonly INorviaPartnerApi _partnerApi = Substitute.For<INorviaPartnerApi>();
    private readonly IPersonaDirectory _personas = Substitute.For<IPersonaDirectory>();
    private readonly SubmitCarrierInvoiceHandler _handler;

    public SubmitCarrierInvoiceHandlerTests()
    {
        _personas.Find(Samples.VanDijkTransport.Id).Returns(Samples.VanDijkTransport);
        _personas.Find(Samples.BrouwerDranken.Id).Returns(Samples.BrouwerDranken);
        _partnerApi.SubmitCarrierInvoiceAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>()).Returns(CarrierInvoiceReply.Received("norvia-1", Receipt));
        _handler = new SubmitCarrierInvoiceHandler(_repository, _partnerApi, _personas, new FakeTimeProvider(Samples.Now));
    }

    private static SubmitCarrierInvoiceCommand Command(
        string number = " INV-1 ", PersonaId? persona = null, params CarrierInvoiceLineDraft[] lines) =>
        new(persona ?? CarrierInvoiceSamples.Persona, number, new DateOnly(2026, 9, 24), lines.Length == 0 ? [new("VDT-1", 100m, "Monday"), new("VDT-2", 90m)] : lines);

    [Fact]
    public async Task Handle_SendsTheInvoiceAsThePersona_AndKeepsWhatNorviaMadeOfEachLine()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        var sent = result.Value;
        sent.InvoiceNumber.Should().Be("INV-1");
        sent.Reply.Result.Should().Be(CarrierInvoiceReplyResult.Received);
        sent.Invoice!.Lines.Select(line => (line.BookingRef, line.Result)).Should().Equal(
            ("VDT-1", CarrierInvoiceLineResult.Matched), ("VDT-2", CarrierInvoiceLineResult.PriceMismatch));
        await _partnerApi.Received(1).SubmitCarrierInvoiceAsync(
            Samples.VanDijkTransport, "INV-1", new DateOnly(2026, 9, 24), Arg.Is<IReadOnlyList<CarrierInvoiceLineDraft>>(lines => lines.Count == 2), Arg.Any<CancellationToken>());
        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierInvoice>(invoice =>
                invoice.PersonaId == Samples.VanDijkTransport.Id
                && invoice.InvoiceNumber == "INV-1"
                && invoice.CorrelationId == "norvia-1"
                && invoice.SubmittedAt == Samples.Now
                && invoice.Status == CarrierInvoiceStatus.Received
                && invoice.Lines[0].Description == "Monday"
                && invoice.Lines[1].Description == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnInvoiceNorviaApprovedOnReceipt_IsKeptApproved()
    {
        _partnerApi.SubmitCarrierInvoiceAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>()).Returns(CarrierInvoiceReply.Received(
            "norvia-1", new CarrierInvoiceReceipt(false, CarrierInvoiceStatus.Approved, 100m, [new("VDT-1", 100m, CarrierInvoiceLineResult.Matched)])));

        await _handler.Handle(Command(lines: new CarrierInvoiceLineDraft("VDT-1", 100m)), CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<CarrierInvoice>(invoice => invoice.Status == CarrierInvoiceStatus.Approved && invoice.ApprovedAmount == 100m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TheSameInvoiceAgain_IsNotKeptTwice()
    {
        _repository.FirstOrDefaultAsync(Arg.Any<CarrierInvoiceByNumberSpec>(), Arg.Any<CancellationToken>())
            .Returns(CarrierInvoiceSamples.Invoice("INV-1", "VDT-1", "VDT-2"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Invoice!.InvoiceNumber.Should().Be("INV-1");
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(CarrierInvoiceReplyResult.InvoiceNumberUsed)]
    [InlineData(CarrierInvoiceReplyResult.CarrierNotFound)]
    [InlineData(CarrierInvoiceReplyResult.Invalid)]
    [InlineData(CarrierInvoiceReplyResult.Refused)]
    public async Task Handle_AnInvoiceNorviaRefused_IsNotKept_AndTheReplyIsReturned(CarrierInvoiceReplyResult refusal)
    {
        _partnerApi.SubmitCarrierInvoiceAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>()).Returns(CarrierInvoiceReply.Refusal(refusal, "No.", "norvia-1"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Reply.Result.Should().Be(refusal);
        result.Value.Invoice.Should().BeNull();
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AnInvoiceNorviaDidNotReceive_IsNotKept_SoItCanBeSentAgain()
    {
        _partnerApi.SubmitCarrierInvoiceAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>()).Returns(CarrierInvoiceReply.NotReceived(null));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        result.Value.Reply.Result.Should().Be(CarrierInvoiceReplyResult.NotReceived);
        await _repository.DidNotReceive().AddAsync(Arg.Any<CarrierInvoice>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678901234567890123456789012345678901234567890X")]
    public async Task Handle_WithoutAUsableInvoiceNumber_IsInvalid_AndSendsNothing(string number)
    {
        var result = await _handler.Handle(Command(number), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Invalid);
        await _partnerApi.DidNotReceive().SubmitCarrierInvoiceAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNoLine_IsInvalid() =>
        (await _handler.Handle(new SubmitCarrierInvoiceCommand(CarrierInvoiceSamples.Persona, "INV-1", new DateOnly(2026, 9, 24), []), CancellationToken.None))
            .Status.Should().Be(ResultStatus.Invalid);

    [Fact]
    public async Task Handle_ALineWithoutAReference_IsInvalid() =>
        (await _handler.Handle(Command(lines: new CarrierInvoiceLineDraft(" ", 10m)), CancellationToken.None)).Status.Should().Be(ResultStatus.Invalid);

    [Fact]
    public async Task Handle_AReferenceTwice_IsInvalid() =>
        (await _handler.Handle(Command(lines: [new("VDT-1", 10m), new(" VDT-1", 20m)]), CancellationToken.None)).Status.Should().Be(ResultStatus.Invalid);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.005)]
    public async Task Handle_AnAmountNotAboveZeroToTheCent_IsInvalid(double amount) =>
        (await _handler.Handle(Command(lines: new CarrierInvoiceLineDraft("VDT-1", (decimal)amount)), CancellationToken.None)).Status.Should().Be(ResultStatus.Invalid);

    [Theory]
    [InlineData("brouwer-dranken")]
    [InlineData("nobody")]
    public async Task Handle_ForNoCarrierPersona_IsNotFound(string persona)
    {
        var result = await _handler.Handle(Command(persona: new PersonaId(persona)), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
        await _partnerApi.DidNotReceive().SubmitCarrierInvoiceAsync(Arg.Any<Persona>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<CarrierInvoiceLineDraft>>(), Arg.Any<CancellationToken>());
    }
}
