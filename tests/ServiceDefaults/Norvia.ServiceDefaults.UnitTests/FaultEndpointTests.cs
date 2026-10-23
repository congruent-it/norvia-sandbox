using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.ServiceDefaults.UnitTests;

public sealed class FaultEndpointTests : IAsyncLifetime
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly FaultDefinition Unavailable = new("driver-app-unavailable", "driver-app", "Refuses every Trip.");

    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.AddFaults(Unavailable);
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, RolesInHeader>("Test", null);
        builder.Services.AddAuthorization();
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapGroup("/v1/faults").MapFaults();
        await _app.StartAsync(Ct);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? roles, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (roles is not null)
        {
            request.Headers.Add(RolesInHeader.Header, roles);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            return await _client.SendAsync(request, Ct);
        }
    }

    [Fact]
    public void EveryFaultEndpoint_RequiresTheDemoFaultRole()
    {
        var endpoints = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

        endpoints.Should().HaveCount(2);
        endpoints.Should().OnlyContain(endpoint => endpoint.Metadata.OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Any()
            || endpoint.Metadata.OfType<Microsoft.AspNetCore.Authorization.AuthorizationPolicy>().Any(policy =>
                policy.Requirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>()
                    .Any(requirement => requirement.AllowedRoles.Contains(FaultRoles.DemoFault))));
    }

    [Theory]
    [InlineData("GET", "/v1/faults", null)]
    [InlineData("PUT", "/v1/faults/driver-app-unavailable", """{"on":true,"durationSeconds":60}""")]
    public async Task WithoutAToken_TheAnswerIs401(string method, string path, string? body)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        using var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _app.Services.GetRequiredService<IFaults>().IsOn(Unavailable.Name).Should().BeFalse();
    }

    [Theory]
    [InlineData("GET", "/v1/faults")]
    [InlineData("PUT", "/v1/faults/driver-app-unavailable")]
    public async Task WithoutTheRole_TheAnswerIs403_AndNothingChanges(string method, string path)
    {
        using var response = await SendAsync(Request(new HttpMethod(method), path, roles: "Estate.Reset,DriverReports.ReportOutcome", body: new { on = true, durationSeconds = 60 }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _app.Services.GetRequiredService<IFaults>().IsOn(Unavailable.Name).Should().BeFalse();
    }

    [Fact]
    public async Task WithTheRole_ListsTheFaults()
    {
        using var response = await SendAsync(Request(HttpMethod.Get, "/v1/faults", FaultRoles.DemoFault));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var faults = await response.Content.ReadFromJsonAsync<List<FaultStatus>>(Ct);
        faults.Should().ContainSingle().Which.Should().Be(new FaultStatus(Unavailable.Name, "driver-app", Unavailable.Description, false, null, null));
    }

    [Fact]
    public async Task WithTheRole_APutSwitchesTheFaultOn_ForTheDuration()
    {
        using var response = await SendAsync(Request(HttpMethod.Put, "/v1/faults/driver-app-unavailable", FaultRoles.DemoFault, new { on = true, durationSeconds = 90 }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await response.Content.ReadFromJsonAsync<FaultStatus>(Ct);
        status!.On.Should().BeTrue();
        status.RemainingSeconds.Should().Be(90);
        _app.Services.GetRequiredService<IFaults>().IsOn(Unavailable.Name).Should().BeTrue();
    }

    [Fact]
    public async Task WithoutADuration_TheFaultIsOnForTheDefault()
    {
        using var response = await SendAsync(Request(HttpMethod.Put, "/v1/faults/driver-app-unavailable", FaultRoles.DemoFault, new { on = true }));

        var status = await response.Content.ReadFromJsonAsync<FaultStatus>(Ct);
        status!.RemainingSeconds.Should().Be((int)_app.Services.GetRequiredService<IOptions<FaultOptions>>().Value.DefaultDuration.TotalSeconds);
    }

    [Fact]
    public async Task WithTheRole_APutCanSwitchTheFaultOff()
    {
        using (await SendAsync(Request(HttpMethod.Put, "/v1/faults/driver-app-unavailable", FaultRoles.DemoFault, new { on = true, durationSeconds = 90 })))
        {
        }

        using var response = await SendAsync(Request(HttpMethod.Put, "/v1/faults/driver-app-unavailable", FaultRoles.DemoFault, new { on = false }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _app.Services.GetRequiredService<IFaults>().IsOn(Unavailable.Name).Should().BeFalse();
    }

    [Fact]
    public async Task ADurationBeyondTheLimit_Is400_AndLeavesTheFaultOff()
    {
        using var response = await SendAsync(Request(HttpMethod.Put, "/v1/faults/driver-app-unavailable", FaultRoles.DemoFault, new { on = true, durationSeconds = 3600 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _app.Services.GetRequiredService<IFaults>().IsOn(Unavailable.Name).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnUnknownFault_Is404(bool on)
    {
        using var response = await SendAsync(Request(HttpMethod.Put, "/v1/faults/nope", FaultRoles.DemoFault, new { on, durationSeconds = 60 }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Stands in for the JWT bearer handler: no header is no token (401), and the header lists the roles of the token.</summary>
    private sealed class RolesInHeader(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Header = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(Header, out var roles))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(roles.ToString().Split(',').Select(role => new Claim(ClaimTypes.Role, role)), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
