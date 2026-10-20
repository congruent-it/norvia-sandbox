using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierInvoices;
using Norvia.PartnerPortal.Application.CarrierInvoices.Submit;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.IntegrationTests;

/// <summary>
/// A Carrier Persona's Carrier invoicing at the portal's boundary (E2, user stories 7 and 9): the draft invoice screen lists its delivered, not yet invoiced bookings with their
/// agreed prices, the invoice outcome endpoint only acknowledges and stores an outcome once per invoice number, and the invoices page shows it. Each test books, delivers and
/// invoices numbers of its own, so none depends on the others.
/// </summary>
public sealed class CarrierInvoiceTests(PartnerPortalFixture fixture)
{
    private const string Carrier = "van-dijk-transport";
    private const string CarrierPartyId = "CAR-0001";
    private const string OtherCarrier = "kessler-spedition";
    private const string OtherCarrierPartyId = "CAR-0002";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PortalWebHost Host => fixture.ResetOn;

    // --- The draft invoice screen ---

    [Fact]
    public async Task TheDraft_ListsTheDeliveredBookingsNotInvoicedYet_WithTheirAgreedPrices()
    {
        var delivered = await DeliveredBookingAsync(price: 301.40m);

        var draft = (await Host.Portal.GetCarrierInvoiceDraftAsync(new PersonaId(Carrier), Ct))!;

        var line = draft.Lines.Should().ContainSingle(line => line.BookingRef == delivered.Reference).Subject;
        line.CarrierBookingNumber.Should().Be(delivered.BookingNumber);
        line.ShipmentNumber.Should().Be(delivered.ShipmentNumber);
        line.AgreedAmount.Should().Be(301.40m);
        draft.SuggestedInvoiceNumber.Should().StartWith("INV-");
    }

    [Fact]
    public async Task TheDraft_LeavesOutABookingThatIsConfirmedButNotDeliveredYet_AndOneOfAnotherCarrier()
    {
        var confirmed = await ConfirmedBookingAsync(price: 120m);
        var other = await DeliveredBookingAsync(price: 99m, carrier: OtherCarrier, partyId: OtherCarrierPartyId);

        var draft = (await Host.Portal.GetCarrierInvoiceDraftAsync(new PersonaId(Carrier), Ct))!;

        draft.Lines.Should().NotContain(line => line.BookingRef == confirmed.Reference || line.BookingRef == other.Reference);
    }

    [Fact]
    public async Task TheDraft_LeavesOutABookingOnceItIsInvoiced_AndTheInvoicesPageShowsTheResultOfItsLine()
    {
        var delivered = await DeliveredBookingAsync(price: 100m);
        var number = NewNumber();

        var submitted = await Host.Portal.SubmitCarrierInvoiceAsync(
            new PersonaId(Carrier), number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineDraft(delivered.Reference, 100m)], Ct);

        submitted.IsSuccess.Should().BeTrue();
        (await Host.Portal.GetCarrierInvoiceDraftAsync(new PersonaId(Carrier), Ct))!.Lines.Should().NotContain(line => line.BookingRef == delivered.Reference);
        var shown = (await InvoicesAsync(Carrier)).Should().ContainSingle(invoice => invoice.InvoiceNumber == number).Subject;
        shown.SentFromPortal.Should().BeTrue();
        shown.Total.Should().Be(100m);
        shown.Lines.Single().Result.Should().Be(CarrierInvoiceLineResult.Matched);
        shown.Lines.Single().Outcome.Should().BeNull("Norvia hasn't told the outcome yet");
    }

    [Fact]
    public async Task ALineNorviaRejected_IsBackInTheDraft_ToInvoiceAgainWithANewInvoiceNumber()
    {
        var delivered = await DeliveredBookingAsync(price: 100m);
        var number = NewNumber();
        await Host.Portal.SubmitCarrierInvoiceAsync(new PersonaId(Carrier), number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineDraft(delivered.Reference, 120m)], Ct);

        using var outcome = await SendOutcomeAsync(Outcome(number, 0m, Line(delivered.Reference, "REJECTED", 0m, "Not what we agreed.")));

        outcome.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Host.Portal.GetCarrierInvoiceDraftAsync(new PersonaId(Carrier), Ct))!.Lines.Should().ContainSingle(line => line.BookingRef == delivered.Reference);
    }

    [Fact]
    public async Task ALineNorviaAcceptedAtAnAdjustedAmount_StaysOutOfTheDraft()
    {
        var delivered = await DeliveredBookingAsync(price: 100m);
        var number = NewNumber();
        await Host.Portal.SubmitCarrierInvoiceAsync(new PersonaId(Carrier), number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineDraft(delivered.Reference, 120m)], Ct);

        using var outcome = await SendOutcomeAsync(Outcome(number, 100m, Line(delivered.Reference, "ACCEPTED_ADJUSTED", 100m, "Waiting time was not agreed.")));

        outcome.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Host.Portal.GetCarrierInvoiceDraftAsync(new PersonaId(Carrier), Ct))!.Lines.Should().NotContain(line => line.BookingRef == delivered.Reference);
    }

    // --- The invoice outcome endpoint ---

    [Fact]
    public async Task AnOutcome_IsAcknowledged_AndShownOnTheInvoicesPage_WithTheOutcomeOfEachLine()
    {
        var first = await DeliveredBookingAsync(price: 100m);
        var second = await DeliveredBookingAsync(price: 80m);
        var number = NewNumber();
        await Host.Portal.SubmitCarrierInvoiceAsync(
            new PersonaId(Carrier), number, new DateOnly(2026, 10, 19),
            [new CarrierInvoiceLineDraft(first.Reference, 100m), new CarrierInvoiceLineDraft(second.Reference, 95m)], Ct);

        using var response = await SendOutcomeAsync(Outcome(
            number, 180m, Line(first.Reference, "MATCHED", 100m), Line(second.Reference, "ACCEPTED_ADJUSTED", 80m, "Waiting time was not agreed.")));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await response.Content.ReadFromJsonAsync<JsonObject>(Ct))!["repeated"]!.GetValue<bool>().Should().BeFalse();
        var shown = (await InvoicesAsync(Carrier)).Should().ContainSingle(invoice => invoice.InvoiceNumber == number).Subject;
        shown.OutcomeTold.Should().BeTrue();
        shown.Status.Should().Be(CarrierInvoiceStatus.Approved);
        shown.ApprovedAmount.Should().Be(180m);
        shown.CorrelationId.Should().NotBeNullOrEmpty();
        var matched = shown.Lines.Single(line => line.BookingRef == first.Reference).Outcome!;
        matched.Result.Should().Be(CarrierInvoiceOutcomeResult.Matched);
        matched.AcceptedAmount.Should().Be(100m);
        var adjusted = shown.Lines.Single(line => line.BookingRef == second.Reference);
        adjusted.Amount.Should().Be(95m, "what the Carrier claimed stays beside what Norvia accepted");
        adjusted.Outcome!.Result.Should().Be(CarrierInvoiceOutcomeResult.AcceptedAdjusted);
        adjusted.Outcome.AcceptedAmount.Should().Be(80m);
        adjusted.Outcome.Remark.Should().Be("Waiting time was not agreed.");
    }

    [Fact]
    public async Task AnOutcomeThatRepeatsOneThePersonaHas_IsAcknowledgedAsARepeat_AndStoredOnce()
    {
        var number = NewNumber();
        var body = Outcome(number, 100m, Line("TRX-REPEAT", "MATCHED", 100m));
        using var first = await SendOutcomeAsync(body);

        using var repeat = await SendOutcomeAsync(Outcome(number, 100m, Line("TRX-REPEAT", "MATCHED", 100m)));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await repeat.Content.ReadFromJsonAsync<JsonObject>(Ct))!["repeated"]!.GetValue<bool>().Should().BeTrue();
        (await InvoicesAsync(Carrier)).Where(invoice => invoice.InvoiceNumber == number).Should().ContainSingle();
    }

    [Fact]
    public async Task TheSameOutcomeTwiceAtOnce_IsStoredOnce()
    {
        var number = NewNumber();

        var responses = await Task.WhenAll(
            SendOutcomeAsync(Outcome(number, 100m, Line("TRX-RACE", "MATCHED", 100m))),
            SendOutcomeAsync(Outcome(number, 100m, Line("TRX-RACE", "MATCHED", 100m))));

        responses.Should().OnlyContain(response => response.StatusCode == HttpStatusCode.OK);
        (await InvoicesAsync(Carrier)).Where(invoice => invoice.InvoiceNumber == number).Should().ContainSingle();
    }

    [Fact]
    public async Task AnOutcomeForAnInvoiceThePortalNeverSent_IsListedFromTheOutcomeAlone()
    {
        var number = NewNumber();

        using var response = await SendOutcomeAsync(Outcome(number, 55m, Line("TRX-SIM", "ACCEPTED", 55m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var shown = (await InvoicesAsync(Carrier)).Should().ContainSingle(invoice => invoice.InvoiceNumber == number).Subject;
        shown.SentFromPortal.Should().BeFalse("the Simulator sent it through the Partner API");
        shown.Total.Should().BeNull();
        shown.ApprovedAmount.Should().Be(55m);
        shown.Lines.Single().Outcome!.Result.Should().Be(CarrierInvoiceOutcomeResult.Accepted);
    }

    [Fact]
    public async Task AnOutcome_IsShownOnlyOnTheInvoicesPageOfTheCarrierItWasSentTo()
    {
        var number = NewNumber();

        using var response = await SendOutcomeAsync(Outcome(number, 55m, Line("TRX-ONLY", "ACCEPTED", 55m)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await InvoicesAsync(OtherCarrier)).Should().NotContain(invoice => invoice.InvoiceNumber == number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-key")]
    public async Task AnOutcomeWithoutTheCarriersCredential_IsRefused_AndStoredNowhere(string? key)
    {
        var number = NewNumber();

        using var response = await SendOutcomeAsync(Outcome(number, 55m, Line("TRX-KEY", "ACCEPTED", 55m)), apiKey: key);

        // The portal answers 401, which its status code pages re-execute as the POST of a page and so turn into 400 when the answer has no body: the same for every
        // endpoint of the portal that asks for a key. Either way the caller is refused.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        (await InvoicesAsync(Carrier)).Should().NotContain(invoice => invoice.InvoiceNumber == number);
    }

    [Fact]
    public async Task AnOutcomeForACarrierThePortalDoesNotHave_IsRefusedAsAnUnknownCaller()
    {
        var number = NewNumber();

        using var response = await SendOutcomeAsync(Outcome(number, 55m, Line("TRX-UNKNOWN", "ACCEPTED", 55m)), partyId: "CAR-9999");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        (await InvoicesAsync(Carrier)).Should().NotContain(invoice => invoice.InvoiceNumber == number);
    }

    [Fact]
    public async Task AnOutcomeWithoutACorrelationId_IsRefusedWith400()
    {
        var number = NewNumber();

        using var response = await SendOutcomeAsync(Outcome(number, 55m, Line("TRX-CORR", "ACCEPTED", 55m)), correlationId: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await InvoicesAsync(Carrier)).Should().NotContain(invoice => invoice.InvoiceNumber == number);
    }

    [Fact]
    public async Task AnOutcomeThatBreaksTheFormat_IsRefusedWith400_AndStoredNowhere()
    {
        foreach (var body in new[]
        {
            Outcome(NewNumber(), 55m),
            Outcome(NewNumber(), 55m, Line("TRX-FMT", "PAID", 55m)),
            Outcome(NewNumber(), 55m, Line("TRX-FMT", "ACCEPTED", -1m)),
            Outcome(NewNumber(), 55m, Line("TRX-FMT", "ACCEPTED", 55.123m)),
            Outcome(NewNumber(), -55m, Line("TRX-FMT", "ACCEPTED", 55m)),
            Outcome(NewNumber(), 55m, Line(" ", "ACCEPTED", 55m)),
        })
        {
            using var response = await SendOutcomeAsync(body);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, because: body.ToJsonString());
            (await InvoicesAsync(Carrier)).Should().NotContain(invoice => invoice.InvoiceNumber == body["invoiceNo"]!.GetValue<string>());
        }
    }

    // --- Setting up ---

    private async Task<IReadOnlyList<CarrierInvoiceView>> InvoicesAsync(string persona) =>
        await Host.Portal.ListCarrierInvoicesAsync(new PersonaId(persona), Ct);

    /// <summary>
    /// A Confirmed booking of the Carrier Persona, as Norvia's booking Logic App sends it and the Persona answers it by hand, so the portal's own workers
    /// don't answer or report on it in the middle of the test.
    /// </summary>
    private async Task<BookedShipment> ConfirmedBookingAsync(decimal price, string carrier = Carrier, string partyId = CarrierPartyId)
    {
        var persona = new PersonaId(carrier);
        await Host.Portal.ChangeCarrierAnswerSettingsAsync(persona, new CarrierAnswerSettingsView(CarrierAnswerMode.Manual, CarrierRejectionReason.Other, 30), Ct);
        await Host.Portal.ChangeCarrierTrackingSettingsAsync(persona, new CarrierTrackingSettingsView(TrackingMode.Manual, 30), Ct);

        var suffix = Random.Shared.Next(0, 10_000_000);
        var booked = new BookedShipment(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"CB-{suffix:D7}"),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"SH-{suffix:D7}"),
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TRX-{suffix:D7}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/carrier/v1/{partyId}/bookings", UriKind.Relative))
        {
            Content = JsonContent.Create(BookingRequest(booked, price)),
        };
        request.Headers.Add("X-Api-Key", PartnerPortalFixture.CarrierKey);
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        using var received = await Host.Http.SendAsync(request, Ct);
        received.StatusCode.Should().Be(HttpStatusCode.OK, await received.Content.ReadAsStringAsync(Ct));

        var answered = await Host.Portal.AnswerCarrierBookingAsync(persona, booked.BookingNumber, new CarrierAnswer.Confirmation(booked.Reference), Ct);
        answered.Should().NotBeNull();
        answered!.Request.Status.Should().Be(CarrierBookingRequestStatus.Confirmed);
        return booked;
    }

    /// <summary>A booking the Carrier Persona confirmed and reported Delivered: what it may invoice.</summary>
    private async Task<BookedShipment> DeliveredBookingAsync(decimal price, string carrier = Carrier, string partyId = CarrierPartyId)
    {
        var booked = await ConfirmedBookingAsync(price, carrier, partyId);

        var sent = await Host.Portal.SendTrackingEventAsync(new PersonaId(carrier), booked.BookingNumber, TrackingEventType.Delivered, Ct);

        sent.Should().NotBeNull();
        sent!.Reply.IsAccepted.Should().BeTrue();
        sent.Recorded.Should().BeTrue();
        return booked;
    }

    private static JsonObject BookingRequest(BookedShipment booked, decimal price) => new()
    {
        ["carrierBookingNumber"] = booked.BookingNumber,
        ["shipmentNumber"] = booked.ShipmentNumber,
        ["collectionStreet"] = "Havenkade 1",
        ["collectionPostcode"] = "3011 AA",
        ["collectionCity"] = "Rotterdam",
        ["collectionCountry"] = "NL",
        ["collectFrom"] = "2026-10-15T08:00:00",
        ["collectUntil"] = "2026-10-15T12:00:00",
        ["deliveryStreet"] = "Marktplein 2",
        ["deliveryPostcode"] = "1011 AB",
        ["deliveryCity"] = "Amsterdam",
        ["deliveryCountry"] = "NL",
        ["deliverFrom"] = "2026-10-16T08:00:00",
        ["deliverUntil"] = "2026-10-16T12:00:00",
        ["consigneeName"] = "Tuincentrum Groenrijk",
        ["goods"] = new JsonArray(new JsonObject { ["packageType"] = "Pallet", ["quantity"] = 2, ["weightKg"] = 400, ["lengthCm"] = 120, ["widthCm"] = 80, ["heightCm"] = 100 }),
        ["carrierCost"] = new JsonObject
        {
            ["currency"] = "EUR",
            ["total"] = price,
            ["chargeLines"] = new JsonArray(new JsonObject { ["type"] = "Freight", ["amount"] = price }),
            ["isSpot"] = false,
        },
    };

    private static string NewNumber() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"INV-2099-{Random.Shared.Next(0, 1_000_000):D6}");

    private static JsonObject Outcome(string invoiceNumber, decimal approvedAmount, params JsonObject[] lines) => new()
    {
        ["invoiceNo"] = invoiceNumber,
        ["approvedAmount"] = approvedAmount,
        ["lines"] = new JsonArray([.. lines.Select(line => (JsonNode)line)]),
    };

    private static JsonObject Line(string bookingRef, string result, decimal acceptedAmount, string? remark = null) => new()
    {
        ["bookingRef"] = bookingRef,
        ["result"] = result,
        ["acceptedAmount"] = acceptedAmount,
        ["remark"] = remark,
    };

    private async Task<HttpResponseMessage> SendOutcomeAsync(
        JsonObject body, string? apiKey = PartnerPortalFixture.CarrierKey, string? correlationId = "", string partyId = CarrierPartyId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/carrier/v1/{partyId}/invoice-outcomes", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        // An empty default stands for the invoice's own correlation ID, so a test names one only to leave it out.
        if (correlationId is not null)
        {
            request.Headers.Add("X-Correlation-ID", correlationId.Length == 0 ? "corr-" + body["invoiceNo"]!.GetValue<string>() : correlationId);
        }

        return await Host.Http.SendAsync(request, Ct);
    }

    private sealed record BookedShipment(string BookingNumber, string ShipmentNumber, string Reference);
}
