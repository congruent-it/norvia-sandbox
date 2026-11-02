using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Norvia.DriverApp.Web.IntegrationTests;

/// <summary>The Driver app's boundary: what a browser gets, signed in as Easy Auth signs it in or as the local stand-in does (ADR 0037).</summary>
public sealed class SignInTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirects = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task GetMyTrips_BehindEasyAuth_ShowsOnlyTheTripsOfTheDriverIdInTheToken()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var page = await GetAsync(client, "/", ClientPrincipal(DriverAppWebFactory.AnoukId));

        page.Should().Contain("TR-0000001").And.Contain("TR-0000002").And.NotContain("TR-0000003");
    }

    [Fact]
    public async Task GetMyTrips_BehindEasyAuth_GroupsTheTripsByDay_WithTheirStatus()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var page = await GetAsync(client, "/", ClientPrincipal(DriverAppWebFactory.AnoukId));

        page.IndexOf("Tuesday 13 October 2026", StringComparison.Ordinal).Should().BePositive()
            .And.BeLessThan(page.IndexOf("Wednesday 14 October 2026", StringComparison.Ordinal));
        page.Should().Contain("Dispatched");
    }

    [Fact]
    public async Task GetMyTrips_BehindEasyAuth_NeverTakesTheDriverFromThePayload()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var page = await GetAsync(client, $"/?driver={DriverAppWebFactory.JoostId}", ClientPrincipal(DriverAppWebFactory.AnoukId));

        page.Should().NotContain("TR-0000003");
    }

    [Fact]
    public async Task GetMyTrips_BehindEasyAuth_WithoutASignIn_SendsTheBrowserToEntra()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().StartWith("/.auth/login/aad?post_login_redirect_uri=");
    }

    [Fact]
    public async Task GetMyTrips_BehindEasyAuth_ForAUserWithoutADriverId_ExplainsAndShowsNoTrips()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var response = await Send(client, "/", ClientPrincipal());

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/refused");
        var refusal = await GetAsync(client, "/refused", ClientPrincipal());
        refusal.Should().Contain("no Driver ID").And.NotContain("TR-0000");
    }

    [Fact]
    public async Task GetMyTrips_BehindEasyAuth_ForAUserWithTwoDriverIds_IsRefusedRatherThanGivenOne()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var response = await Send(client, "/", ClientPrincipal(DriverAppWebFactory.AnoukId, DriverAppWebFactory.JoostId));

        response.Headers.Location!.OriginalString.Should().Be("/refused");
    }

    [Fact]
    public async Task GetHealth_WithoutASignIn_Answers()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMyTrips_WithTheStandIn_SendsTheBrowserToItsSignInPage_WhichListsTheSeededDrivers()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = app.CreateClient(NoRedirects);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);
        var signIn = await client.GetStringAsync(new Uri("/local/sign-in", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.Location!.PathAndQuery.Should().StartWith("/local/sign-in");
        signIn.Should().Contain("Anouk de Vries").And.Contain(DriverAppWebFactory.AnoukId);
    }

    [Fact]
    public async Task GetMyTrips_AfterSigningInToTheStandIn_ShowsOnlyThatDriversTrips()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

        var signedIn = await client.PostAsync(
            new Uri("/local/sign-in", UriKind.Relative),
            new FormUrlEncodedContent([new("driver", DriverAppWebFactory.JoostId)]),
            TestContext.Current.CancellationToken);
        var page = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        signedIn.StatusCode.Should().Be(HttpStatusCode.Redirect);
        page.Should().Contain("TR-0000003").And.NotContain("TR-0000001");
    }

    [Fact]
    public async Task GetMyTrips_WithTheStandIn_DoesNotTrustAnEasyAuthHeader()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = app.CreateClient(NoRedirects);

        var response = await Send(client, "/", ClientPrincipal(DriverAppWebFactory.AnoukId));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task SignInToTheStandIn_AsAnUnknownDriver_IsRefused()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true);
        using var client = app.CreateClient(NoRedirects);

        var response = await client.PostAsync(
            new Uri("/local/sign-in", UriKind.Relative),
            new FormUrlEncodedContent([new("driver", "DRV-9999")]),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Start_WithTheStandInOutsideDevelopment_Refuses()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: true);

        var starting = () => app.CreateClient();

        starting.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetStandInSignIn_WithoutTheStandIn_IsNotThere()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var response = await Send(client, "/local/sign-in", ClientPrincipal(DriverAppWebFactory.AnoukId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMyTrips_WithThePicker_ForAPresenterWhoHasNotPicked_SendsThemToThePicker()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        var response = await Send(client, "/", PresenterPrincipal());

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/pick-driver");
    }

    [Fact]
    public async Task GetMyTrips_WithThePicker_WithoutASignIn_SendsTheBrowserToEntra()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.Location!.OriginalString.Should().StartWith("/.auth/login/aad?post_login_redirect_uri=");
    }

    [Fact]
    public async Task GetPicker_WithoutASignIn_SendsTheBrowserToEntra()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        var response = await client.GetAsync(new Uri("/pick-driver", UriKind.Relative), TestContext.Current.CancellationToken);

        response.Headers.Location!.OriginalString.Should().StartWith("/.auth/login/aad");
    }

    [Fact]
    public async Task GetPicker_ForAPresenter_ListsTheSeededDrivers()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        var page = await GetAsync(client, "/pick-driver", PresenterPrincipal());

        page.Should().Contain("Anouk de Vries").And.Contain(DriverAppWebFactory.AnoukId).And.Contain("Joost Bakker");
    }

    [Fact]
    public async Task GetMyTrips_WithThePicker_AfterPickingADriver_ShowsOnlyThatDriversTrips_UnderTheirName()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        var page = await GetAsync(client, "/", PresenterPrincipal(), pickedDriver: DriverAppWebFactory.JoostId);

        page.Should().Contain("TR-0000003").And.NotContain("TR-0000001").And.Contain("Joost Bakker").And.Contain("Switch driver");
    }

    [Fact]
    public async Task PostPicker_AsAPresenter_RemembersTheDriver_ForTheNextRequest()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);
        var principal = PresenterPrincipal();
        var form = await GetAsync(client, "/pick-driver", principal, antiforgery: true);

        using var post = new HttpRequestMessage(HttpMethod.Post, new Uri("/pick-driver", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([new("driver", DriverAppWebFactory.JoostId), .. HiddenFields(form.Page)]),
        };
        post.Headers.Add("X-MS-CLIENT-PRINCIPAL", principal);
        post.Headers.Add("Cookie", form.AntiforgeryCookie);
        var response = await client.SendAsync(post, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith("norvia-driver-picked=DRV-0002", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostPicker_WithoutTheAntiforgeryToken_PicksNoDriver()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        using var post = new HttpRequestMessage(HttpMethod.Post, new Uri("/pick-driver", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent([new("driver", DriverAppWebFactory.JoostId)]),
        };
        post.Headers.Add("X-MS-CLIENT-PRINCIPAL", PresenterPrincipal());
        var response = await client.SendAsync(post, TestContext.Current.CancellationToken);

        response.Headers.Contains("Set-Cookie").Should().BeFalse("no Driver is picked without the page's own token");
    }

    [Fact]
    public async Task GetMyTrips_WithThePicker_ForAnUnknownPickedDriver_SendsThemBackToThePicker()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/", UriKind.Relative));
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", PresenterPrincipal());
        request.Headers.Add("Cookie", "norvia-driver-picked=DRV-9999");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.Location!.OriginalString.Should().Be("/pick-driver");
    }

    [Fact]
    public async Task GetMyTrips_WithThePicker_TakesNoDriverIdFromThePresentersRoles()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false, picker: true);
        using var client = app.CreateClient(NoRedirects);

        // A role of the demo UIs' registration, and even a Driver ID role: the Driver comes from the pick, not from the roles.
        var response = await Send(client, "/", ClientPrincipal("DemoUis.Use", DriverAppWebFactory.AnoukId));

        response.Headers.Location!.OriginalString.Should().Be("/pick-driver");
    }

    [Fact]
    public async Task GetPicker_WithoutThePicker_IsNotThere()
    {
        await using var app = new DriverAppWebFactory("Production", standIn: false);
        using var client = app.CreateClient(NoRedirects);

        var response = await Send(client, "/pick-driver", PresenterPrincipal());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Start_WithThePickerAndTheStandIn_Refuses()
    {
        await using var app = new DriverAppWebFactory("Development", standIn: true, picker: true);

        var starting = () => app.CreateClient();

        starting.Should().Throw<InvalidOperationException>().WithMessage("*turn on one*");
        await Task.CompletedTask;
    }

    private static async Task<string> GetAsync(HttpClient client, string path, string principal, string? pickedDriver = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", principal);
        if (pickedDriver is not null)
        {
            request.Headers.Add("Cookie", $"norvia-driver-picked={pickedDriver}");
        }

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<(string Page, string AntiforgeryCookie)> GetAsync(HttpClient client, string path, string principal, bool antiforgery)
    {
        _ = antiforgery;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", principal);
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        return (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), cookie);
    }

    /// <summary>The hidden fields of the picker's form, the antiforgery token among them, as a browser would post them back.</summary>
    private static IEnumerable<KeyValuePair<string, string>> HiddenFields(string page) =>
        System.Text.RegularExpressions.Regex.Matches(page, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]*)\" />")
            .Select(match => new KeyValuePair<string, string>(match.Groups[1].Value, System.Net.WebUtility.HtmlDecode(match.Groups[2].Value)));

    /// <summary>The presenter, signed in by Easy Auth to the demo UIs' registration: a role of that registration and no Driver ID.</summary>
    private static string PresenterPrincipal() => ClientPrincipal("DemoUis.Use");

    private static async Task<string> GetAsync(HttpClient client, string path, string principal)
    {
        var response = await Send(client, path, principal);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, string path, string principal)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", principal);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The X-MS-CLIENT-PRINCIPAL header as Container Apps' Easy Auth sends it for an Entra sign-in; the Driver ID is an app role.</summary>
    private static string ClientPrincipal(params string[] driverIds) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            auth_typ = "aad",
            name_typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            role_typ = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
            claims = new[] { new { typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", val = "Test Driver" } }
                .Concat(driverIds.Select(id => new { typ = "roles", val = id })),
        })));
}
