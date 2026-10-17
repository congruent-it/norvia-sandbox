extern alias PortalWeb;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Norvia.PartnerPortal.Application.CarrierBookings.AnswerSettings;
using Norvia.PartnerPortal.Application.CarrierShipments.TrackingSettings;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Web.IntegrationTests;

/// <summary>
/// The Simulator's reset at the Partner portal (ADR 0039): with the setting on and the Simulator's key, the portal deletes what its Personas sent and
/// received and puts their modes back to the seed file's. The tests share one database, which each of them empties, so they run one at a time.
/// </summary>
public sealed class PartnerPortalResetTests(PartnerPortalFixture fixture)
{
    private const string Shipper = "brouwer-dranken";
    private const string Carrier = "van-dijk-transport";
    private const string CarrierPartyId = "CAR-0001";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AResetWithTheKey_DeletesTheBookingsTheBookingRequestsAndTheModes_AndKeepsThePersonas()
    {
        var portal = fixture.ResetOn.Portal;
        var personasBefore = await portal.ListPersonasAsync(Ct);
        var seededAnswer = (await portal.GetCarrierAnswerSettingsAsync(new PersonaId(Carrier), Ct))!;
        var seededTracking = (await portal.GetCarrierTrackingSettingsAsync(new PersonaId(Carrier), Ct))!;
        await SendBookingAsync(portal);
        await ReceiveBookingRequestAsync(fixture.ResetOn.Http, "VDT-reset-1");
        await portal.ChangeCarrierAnswerSettingsAsync(new PersonaId(Carrier), new CarrierAnswerSettingsView(CarrierAnswerMode.Manual, CarrierRejectionReason.Other, 30), Ct);
        await portal.ChangeCarrierTrackingSettingsAsync(new PersonaId(Carrier), new CarrierTrackingSettingsView(TrackingMode.Manual, 30), Ct);
        (await portal.ListBookingsAsync(new PersonaId(Shipper), Ct)).Should().NotBeEmpty();
        (await portal.ListCarrierBookingRequestsAsync(new PersonaId(Carrier), Ct)).Should().NotBeEmpty();

        var deleted = await ResetAsync(fixture.ResetOn.Http);

        deleted["Bookings"].Should().BePositive();
        deleted["Carrier booking requests"].Should().BePositive();
        deleted["Answer modes"].Should().BePositive();
        deleted["Tracking modes"].Should().BePositive();
        (await portal.ListBookingsAsync(new PersonaId(Shipper), Ct)).Should().BeEmpty();
        (await portal.ListCarrierBookingRequestsAsync(new PersonaId(Carrier), Ct)).Should().BeEmpty();
        (await portal.GetCarrierAnswerSettingsAsync(new PersonaId(Carrier), Ct)).Should().Be(seededAnswer);
        (await portal.GetCarrierTrackingSettingsAsync(new PersonaId(Carrier), Ct)).Should().Be(seededTracking);
        (await portal.ListPersonasAsync(Ct)).Should().BeEquivalentTo(personasBefore);
    }

    [Fact]
    public async Task AResetOnAnEmptyPortal_Succeeds_WithNothingDeleted()
    {
        await ResetAsync(fixture.ResetOn.Http);

        var deleted = await ResetAsync(fixture.ResetOn.Http);

        deleted.Values.Should().OnlyContain(count => count == 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-the-key")]
    public async Task AResetWithoutTheKey_IsRefused_AndDeletesNothing(string? key)
    {
        await SendBookingAsync(fixture.ResetOn.Portal);

        using var response = await PostResetAsync(fixture.ResetOn.Http, key);

        // The portal answers 401, which its status code pages re-execute as the POST of a page and so turn into 400 when the answer has no body: the same for every
        // endpoint of the portal that asks for a key. Either way the caller is refused.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest);
        (await fixture.ResetOn.Portal.ListBookingsAsync(new PersonaId(Shipper), Ct)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task AResetWhileTheSettingIsOff_IsRefusedWith403AndTheResetDisabledCode_AndDeletesNothing()
    {
        await SendBookingAsync(fixture.ResetOff.Portal);

        using var response = await PostResetAsync(fixture.ResetOff.Http, PartnerPortalFixture.ResetKey);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!["code"]!.GetValue<string>().Should().Be("ResetDisabled");
        (await fixture.ResetOff.Portal.ListBookingsAsync(new PersonaId(Shipper), Ct)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task TheReadiness_SaysWhetherTheResetIsSwitchedOn()
    {
        using var on = await GetReadinessAsync(fixture.ResetOn.Http);
        using var off = await GetReadinessAsync(fixture.ResetOff.Http);

        JsonNode.Parse(await on.Content.ReadAsStringAsync(Ct))!["enabled"]!.GetValue<bool>().Should().BeTrue();
        JsonNode.Parse(await off.Content.ReadAsStringAsync(Ct))!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }

    private static async Task SendBookingAsync(PortalWeb::Norvia.PartnerPortal.Web.Bookings.IPartnerPortal portal)
    {
        var draft = await portal.DraftBookingAsync(new PersonaId(Shipper), Ct);
        var sent = await portal.SubmitBookingAsync(new PersonaId(Shipper), draft! with { ShipperReference = $"RESET-{Guid.NewGuid():N}"[..20] }, Ct);
        sent!.Recorded.Should().BeTrue();
    }

    private static async Task ReceiveBookingRequestAsync(HttpClient http, string carrierBookingNumber)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/carrier/v1/{CarrierPartyId}/bookings", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                carrierBookingNumber,
                shipmentNumber = "SH-0000001",
                collectionStreet = "Waalhaven 1",
                collectionPostcode = "3087 BM",
                collectionCity = "Rotterdam",
                collectionCountry = "NL",
                deliveryStreet = "Königsallee 1",
                deliveryPostcode = "40212",
                deliveryCity = "Düsseldorf",
                deliveryCountry = "DE",
                consigneeName = "Bakker BV",
                goods = new[] { new { packageType = "Pallet", quantity = 2, weightKg = 120m } },
            }),
        };
        request.Headers.Add("X-Api-Key", PartnerPortalFixture.CarrierKey);
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        using var response = await http.SendAsync(request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<IReadOnlyDictionary<string, int>> ResetAsync(HttpClient http)
    {
        using var response = await PostResetAsync(http, PartnerPortalFixture.ResetKey);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonNode.Parse(body)!["deleted"]!.AsArray().ToDictionary(kind => kind!["kind"]!.GetValue<string>(), kind => kind!["count"]!.GetValue<int>());
    }

    private static Task<HttpResponseMessage> PostResetAsync(HttpClient http, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/reset/v1", UriKind.Relative));
        if (key is not null)
        {
            request.Headers.Add("X-Api-Key", key);
        }

        return http.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> GetReadinessAsync(HttpClient http)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/reset/v1", UriKind.Relative));
        request.Headers.Add("X-Api-Key", PartnerPortalFixture.ResetKey);
        return http.SendAsync(request, Ct);
    }
}
