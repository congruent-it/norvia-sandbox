using System.Net;
using System.Net.Http.Headers;
using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.TransportOrders.GetRejectedBooking;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>
/// Who may read the Customer service agent's view (ADR 0040): a token of the agent API's scheme, for the agent API's audience, that holds the one
/// read role. The TMS's own tokens, for its own audience, open nothing here, and the view's tokens open nothing else.
/// </summary>
public sealed class AgentApiAuthenticationTests
{
    private const string Issuer = "https://norvia.local/tokens";
    private const string AgentAudience = "api://norvia-tms-agent-api";
    private const string TmsAudience = "api://norvia-tms";
    private const string Role = "TransportOrders.CustomerServiceAgent.Read";
    private const string Path = "/v1/agent/customer-service/rejected-bookings/3f2b8c9e-6d5a-4c1e-9a77-0e1f2a3b4c5d";

    private static readonly byte[] Key = [.. Enumerable.Range(1, 64).Select(i => (byte)i)];

    private static async Task<(WebApplication App, HttpClient Client)> StartAsync(bool configured = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        if (configured)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agents:Api:Issuer"] = Issuer,
                ["Agents:Api:Audience"] = AgentAudience,
                ["Agents:Api:SigningKey"] = Convert.ToBase64String(Key),
            });
        }

        builder.AddAgentApi();
        builder.Services.AddAuthorization();
        builder.Services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader());
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute's Returns takes the call's ValueTask as the call being configured.
        mediator.Send(Arg.Any<GetRejectedBookingQuery>(), Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(Result<Norvia.Tms.Application.TransportOrders.TransportOrderView>.NotFound()));
#pragma warning restore CA2012
        builder.Services.AddSingleton(mediator);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapCustomerServiceAgent();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static string Token(string audience = AgentAudience, string[]? roles = null, byte[]? key = null, string issuer = Issuer)
    {
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now.AddMinutes(-1),
            Expires = now.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = "agent", ["roles"] = roles ?? [Role] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? Key), SecurityAlgorithms.HmacSha256),
        });
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Get_WithATokenForTheAgentApiHoldingTheRole_ReachesTheView()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        (await GetAsync(client, Token())).Should().Be(HttpStatusCode.NotFound, "the token passed and the view found no such Rejected booking");
    }

    [Fact]
    public async Task Get_WithNoToken_IsUnauthorized()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        (await GetAsync(client, null)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithATokenForTheTmsOwnAudience_IsUnauthorized()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        (await GetAsync(client, Token(audience: TmsAudience))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WithATokenWithoutTheRole_IsForbidden()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        (await GetAsync(client, Token(roles: ["TransportOrders.Manage"]))).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_WithATokenSignedByAnotherKey_IsUnauthorized()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        using var __ = client;

        (await GetAsync(client, Token(key: [.. Enumerable.Range(100, 64).Select(i => (byte)i)]))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_WhenTheTmsIsToldNothingAboutWhomToTrust_TrustsNoOne()
    {
        var (app, client) = await StartAsync(configured: false);
        await using var _ = app;
        using var __ = client;

        (await GetAsync(client, Token())).Should().Be(HttpStatusCode.Unauthorized);
    }
}
