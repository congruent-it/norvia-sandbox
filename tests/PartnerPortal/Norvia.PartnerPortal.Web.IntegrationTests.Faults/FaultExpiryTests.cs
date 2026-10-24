using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Norvia.PartnerPortal.Application.Faults;

namespace Norvia.PartnerPortal.Web.IntegrationTests.Faults;

/// <summary>Each fault of the Partner portal ends by itself: after its duration the Persona does what it did before, with nothing for the presenter to switch off.</summary>
public sealed class FaultExpiryTests(FaultsFixture fixture)
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(3);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Faults =>
    [
        PartnerPortalFaultNames.TrackingUnmappedCode,
        PartnerPortalFaultNames.AnswerUnmappedReason,
        PartnerPortalFaultNames.BookingEndpointDown,
        PartnerPortalFaultNames.ProofHeicPhoto,
        PartnerPortalFaultNames.ShipperEndpointDown,
        PartnerPortalFaultNames.CancellationEndpointDown,
    ];

    public static TheoryData<string, string> EndpointFaults => new()
    {
        { PartnerPortalFaultNames.BookingEndpointDown, $"/carrier/v1/{FaultsFixture.CarrierId}/bookings" },
        { PartnerPortalFaultNames.CancellationEndpointDown, $"/carrier/v1/{FaultsFixture.CarrierId}/cancellations" },
        { PartnerPortalFaultNames.ShipperEndpointDown, $"/shipper/v1/{FaultsFixture.ShipperId}/notifications" },
    };

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task AFault_IsOnForItsDuration_AndOffAfterwards(string fault)
    {
        var switched = await fixture.Portal.SwitchFaultAsync(FaultsFixture.PartnerPortalOwner, fault, Short, Ct);

        switched.On.Should().BeTrue();
        switched.Until.Should().BeCloseTo(DateTimeOffset.UtcNow + Short, TimeSpan.FromSeconds(2));
        await Eventually.UntilAsync(async () => !await IsOnAsync(fault), Ct, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [MemberData(nameof(EndpointFaults))]
    public async Task AnEndpointFault_AnswersAgain_WhenItExpires(string fault, string path)
    {
        await fixture.Portal.SwitchFaultAsync(FaultsFixture.PartnerPortalOwner, fault, Short, Ct);
        using var http = new HttpClient { BaseAddress = fixture.PortalAddress };

        (await PostAsync(http, path)).Should().Be(HttpStatusCode.ServiceUnavailable, "the endpoint is down while the fault is on");

        await Eventually.UntilAsync(async () => await PostAsync(http, path) != HttpStatusCode.ServiceUnavailable, Ct, TimeSpan.FromSeconds(30));
    }

    private async Task<bool> IsOnAsync(string fault) =>
        (await fixture.Portal.ListFaultsAsync(Ct)).Single(view => view.Owner == FaultsFixture.PartnerPortalOwner && view.Name == fault).On;

    // Without the credential: the endpoint answers 503 while it is down, and refuses the caller once it is up.
    private static async Task<HttpStatusCode> PostAsync(HttpClient http, string path)
    {
        using var content = new StringContent("{}", Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        using var response = await http.PostAsync(new Uri(path, UriKind.Relative), content, Ct);
        return response.StatusCode;
    }
}
