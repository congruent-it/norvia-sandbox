using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Domain.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonaId Carrier = new("van-dijk-transport");

    private static CarrierInvoiceLine Line(string reference, decimal amount = 100m, CarrierInvoiceLineResult result = CarrierInvoiceLineResult.Matched) =>
        new(reference, amount, null, result);

    private static CarrierInvoice Invoice(string number, params CarrierInvoiceLine[] lines) =>
        CarrierInvoice.Receive(Carrier, number, new DateOnly(2026, 10, 20), "corr-1", At, CarrierInvoiceStatus.Received, null, lines);

    private static CarrierInvoiceOutcome Outcome(string number, params (string Reference, CarrierInvoiceOutcomeResult Result)[] lines) =>
        CarrierInvoiceOutcome.Receive(Carrier, number, "corr-2", At, 0m, [.. lines.Select(line => new CarrierInvoiceOutcomeLine(line.Reference, line.Result, 0m, null))]);

    [Fact]
    public void Receive_KeepsWhatNorviaAnswered_AndAddsUpTheLines()
    {
        var invoice = Invoice("INV-1", Line("VDT-1", 70.50m), Line("VDT-2", 20m, CarrierInvoiceLineResult.PriceMismatch));

        invoice.PersonaId.Should().Be(Carrier);
        invoice.InvoiceNumber.Should().Be("INV-1");
        invoice.InvoiceDate.Should().Be(new DateOnly(2026, 10, 20));
        invoice.CorrelationId.Should().Be("corr-1");
        invoice.SubmittedAt.Should().Be(At);
        invoice.Status.Should().Be(CarrierInvoiceStatus.Received);
        invoice.Total.Should().Be(90.50m);
    }

    [Fact]
    public void Receive_GivesEachInvoiceAnIdOfItsOwn() =>
        Invoice("INV-1", Line("VDT-1")).Id.Should().NotBe(Invoice("INV-1", Line("VDT-1")).Id);

    [Fact]
    public void Receive_WithoutWhatItNeeds_Throws()
    {
        var withoutPersona = () => CarrierInvoice.Receive(null!, "INV-1", new DateOnly(2026, 10, 20), "corr-1", At, CarrierInvoiceStatus.Received, null, [Line("VDT-1")]);
        var withoutNumber = () => Invoice(" ", Line("VDT-1"));
        var withoutCorrelation = () => CarrierInvoice.Receive(Carrier, "INV-1", new DateOnly(2026, 10, 20), "", At, CarrierInvoiceStatus.Received, null, [Line("VDT-1")]);
        var withoutLines = () => Invoice("INV-1");
        var nullLines = () => CarrierInvoice.Receive(Carrier, "INV-1", new DateOnly(2026, 10, 20), "corr-1", At, CarrierInvoiceStatus.Received, null, null!);

        withoutPersona.Should().Throw<ArgumentNullException>();
        withoutNumber.Should().Throw<ArgumentException>();
        withoutCorrelation.Should().Throw<ArgumentException>();
        withoutLines.Should().Throw<ArgumentException>();
        nullLines.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OutcomeReceive_KeepsWhatNorviaTold_AndWhichLineItRejected()
    {
        var outcome = Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Accepted), ("VDT-2", CarrierInvoiceOutcomeResult.Rejected));

        outcome.PersonaId.Should().Be(Carrier);
        outcome.InvoiceNumber.Should().Be("INV-1");
        outcome.CorrelationId.Should().Be("corr-2");
        outcome.ReceivedAt.Should().Be(At);
        outcome.Rejects("VDT-2").Should().BeTrue();
        outcome.Rejects("VDT-1").Should().BeFalse();
        outcome.Rejects("VDT-9").Should().BeFalse();
    }

    [Fact]
    public void OutcomeReceive_GivesEachOutcomeAnIdOfItsOwn() =>
        Outcome("INV-1").Id.Should().NotBe(Outcome("INV-1").Id);

    [Fact]
    public void OutcomeReceive_WithoutWhatItNeeds_Throws()
    {
        var withoutPersona = () => CarrierInvoiceOutcome.Receive(null!, "INV-1", "corr-2", At, 0m, []);
        var withoutNumber = () => CarrierInvoiceOutcome.Receive(Carrier, "", "corr-2", At, 0m, []);
        var withoutCorrelation = () => CarrierInvoiceOutcome.Receive(Carrier, "INV-1", " ", At, 0m, []);
        var withoutLines = () => CarrierInvoiceOutcome.Receive(Carrier, "INV-1", "corr-2", At, 0m, null!);

        withoutPersona.Should().Throw<ArgumentNullException>();
        withoutNumber.Should().Throw<ArgumentException>();
        withoutCorrelation.Should().Throw<ArgumentException>();
        withoutLines.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Invoiced_CountsEveryReferenceOnAnInvoiceNotYetTold()
    {
        var invoiced = CarrierInvoiced.References([Invoice("INV-1", Line("VDT-1"), Line("VDT-2"))], []);

        invoiced.Should().BeEquivalentTo(["VDT-1", "VDT-2"]);
    }

    [Fact]
    public void Invoiced_FreesAReferenceNorviaRejected_ButNotOneItAccepted()
    {
        var invoices = new[] { Invoice("INV-1", Line("VDT-1"), Line("VDT-2")) };
        var outcomes = new[] { Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Rejected), ("VDT-2", CarrierInvoiceOutcomeResult.AcceptedAdjusted)) };

        CarrierInvoiced.References(invoices, outcomes).Should().BeEquivalentTo(["VDT-2"]);
    }

    [Fact]
    public void Invoiced_ARejectionOnAnotherInvoiceNumber_FreesNothing()
    {
        var invoices = new[] { Invoice("INV-1", Line("VDT-1")) };
        var outcomes = new[] { Outcome("INV-2", ("VDT-1", CarrierInvoiceOutcomeResult.Rejected)) };

        CarrierInvoiced.References(invoices, outcomes).Should().BeEquivalentTo(["VDT-1"]);
    }

    [Fact]
    public void Invoiced_AReferenceInvoicedAgainAfterARejection_StaysInvoiced()
    {
        var invoices = new[] { Invoice("INV-1", Line("VDT-1")), Invoice("INV-2", Line("VDT-1")) };
        var outcomes = new[] { Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Rejected)) };

        CarrierInvoiced.References(invoices, outcomes).Should().BeEquivalentTo(["VDT-1"]);
    }

    [Fact]
    public void Invoiced_WithoutWhatItNeeds_Throws()
    {
        var withoutInvoices = () => CarrierInvoiced.References(null!, []);
        var withoutOutcomes = () => CarrierInvoiced.References([], null!);

        withoutInvoices.Should().Throw<ArgumentNullException>();
        withoutOutcomes.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("MATCHED", true, CarrierInvoiceLineResult.Matched)]
    [InlineData("PRICE_MISMATCH", true, CarrierInvoiceLineResult.PriceMismatch)]
    [InlineData("UNKNOWN_BOOKING", true, CarrierInvoiceLineResult.UnknownBooking)]
    [InlineData("DUPLICATE", true, CarrierInvoiceLineResult.Duplicate)]
    [InlineData("matched", false, default(CarrierInvoiceLineResult))]
    [InlineData(null, false, default(CarrierInvoiceLineResult))]
    public void LineResults_ParseTheWordsOfTheResponseFormat(string? wire, bool known, CarrierInvoiceLineResult expected)
    {
        CarrierInvoiceLineResults.TryParse(wire, out var result).Should().Be(known);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("MATCHED", true, CarrierInvoiceOutcomeResult.Matched)]
    [InlineData("ACCEPTED", true, CarrierInvoiceOutcomeResult.Accepted)]
    [InlineData("ACCEPTED_ADJUSTED", true, CarrierInvoiceOutcomeResult.AcceptedAdjusted)]
    [InlineData("REJECTED", true, CarrierInvoiceOutcomeResult.Rejected)]
    [InlineData("PRICE_MISMATCH", false, default(CarrierInvoiceOutcomeResult))]
    [InlineData(null, false, default(CarrierInvoiceOutcomeResult))]
    public void OutcomeResults_ParseTheWordsOfTheOutcomeFormat(string? wire, bool known, CarrierInvoiceOutcomeResult expected)
    {
        CarrierInvoiceOutcomeResults.TryParse(wire, out var result).Should().Be(known);
        result.Should().Be(expected);
        CarrierInvoiceOutcomeResults.WireNames.Should().HaveCount(4);
    }

    [Fact]
    public void Reply_Received_CarriesWhatNorviaMadeOfTheInvoice()
    {
        var receipt = new CarrierInvoiceReceipt(false, CarrierInvoiceStatus.Received, null, [new("VDT-1", 10m, CarrierInvoiceLineResult.Matched)]);

        var reply = CarrierInvoiceReply.Received("corr-1", receipt);

        reply.Result.Should().Be(CarrierInvoiceReplyResult.Received);
        reply.MessageId.Should().Be("corr-1");
        reply.Receipt.Should().BeSameAs(receipt);
        reply.Text.Should().BeNull();
    }

    [Fact]
    public void Reply_Refusal_CarriesNorviasWords()
    {
        var reply = CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.InvoiceNumberUsed, "Used.", "corr-1");

        reply.Text.Should().Be("Used.");
        reply.Receipt.Should().BeNull();
    }

    [Fact]
    public void Reply_NotReceived_HasNeitherReceiptNorWords()
    {
        var reply = CarrierInvoiceReply.NotReceived(null);

        reply.Result.Should().Be(CarrierInvoiceReplyResult.NotReceived);
        reply.Receipt.Should().BeNull();
        reply.Text.Should().BeNull();
    }

    [Fact]
    public void Reply_BadlyBuilt_Throws()
    {
        var receivedAsRefusal = () => CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.Received, "Used.", null);
        var lostAsRefusal = () => CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.NotReceived, "Used.", null);
        var withoutWords = () => CarrierInvoiceReply.Refusal(CarrierInvoiceReplyResult.Invalid, " ", null);
        var withoutReceipt = () => CarrierInvoiceReply.Received("corr-1", null!);

        receivedAsRefusal.Should().Throw<ArgumentOutOfRangeException>();
        lostAsRefusal.Should().Throw<ArgumentOutOfRangeException>();
        withoutWords.Should().Throw<ArgumentException>();
        withoutReceipt.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DeliveriesSpec_ChoosesTheConfirmedBookingsReportedDelivered_OfThePersona_OldestFirst()
    {
        static CarrierBookingRequest Booking(string number, PersonaId persona, DateTimeOffset received, bool confirm, bool delivered)
        {
            var request = CarrierBookingRequest.Receive(
                persona, number, "SH-1", "release-1", received,
                new CarrierBookingDetails(new CarrierBookingStop(null, null, null, null, null, null), new CarrierBookingStop(null, null, null, null, null, null), null, []));
            if (confirm)
            {
                request.Answer(new CarrierAnswer.Confirmation("REF-" + number), "a", received);
            }

            if (delivered)
            {
                request.ReportTracking(TrackingEventType.Delivered);
            }

            return request;
        }

        var requests = new[]
        {
            Booking("CB-3", Carrier, At.AddHours(3), confirm: true, delivered: true),
            Booking("CB-1", Carrier, At.AddHours(1), confirm: true, delivered: true),
            Booking("CB-2", Carrier, At.AddHours(2), confirm: true, delivered: false),
            Booking("CB-4", Carrier, At.AddHours(4), confirm: false, delivered: false),
            Booking("CB-5", new PersonaId("kessler-spedition"), At.AddHours(5), confirm: true, delivered: true),
        };

        new CarrierDeliveriesOfPersonaSpec(Carrier).Evaluate(requests).Select(request => request.CarrierBookingNumber).Should().Equal("CB-1", "CB-3");
    }

    [Fact]
    public void Specs_ChooseThePersonasOwnInvoicesAndOutcomes_ByNumber()
    {
        var other = CarrierInvoice.Receive(new PersonaId("kessler-spedition"), "INV-1", new DateOnly(2026, 10, 20), "c", At, CarrierInvoiceStatus.Received, null, [Line("K-1")]);
        var invoices = new[] { Invoice("INV-1", Line("VDT-1")), Invoice("INV-2", Line("VDT-2")), other };
        var outcomes = new[]
        {
            Outcome("INV-1", ("VDT-1", CarrierInvoiceOutcomeResult.Matched)),
            CarrierInvoiceOutcome.Receive(new PersonaId("kessler-spedition"), "INV-1", "c", At, 0m, []),
        };

        new CarrierInvoicesOfPersonaSpec(Carrier).Evaluate(invoices).Should().HaveCount(2);
        new CarrierInvoiceByNumberSpec(Carrier, "INV-1").Evaluate(invoices).Should().ContainSingle().Which.Lines[0].BookingRef.Should().Be("VDT-1");
        new CarrierInvoiceOutcomesOfPersonaSpec(Carrier).Evaluate(outcomes).Should().ContainSingle();
        new CarrierInvoiceOutcomeByNumberSpec(Carrier, "INV-1").Evaluate(outcomes).Should().ContainSingle();
        new CarrierInvoiceOutcomeByNumberSpec(Carrier, "INV-9").Evaluate(outcomes).Should().BeEmpty();
    }
}
