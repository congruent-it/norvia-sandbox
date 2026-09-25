using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Norvia.AppHost;

[assembly: AssemblyFixture(typeof(Norvia.Tms.Web.UiTests.TmsUiFixture))]

namespace Norvia.Tms.Web.UiTests;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run (TMS API, TMS UI, SQL and the Service Bus emulator) and a
/// headless Chromium to drive the TMS UI. It holds the key the TMS trusts locally, so <see cref="Intake"/> submits
/// Transport Orders the way the intake Logic App does.
/// </summary>
public sealed class TmsUiFixture : IAsyncLifetime
{
    private const string IntakeRole = "TransportOrders.Intake";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    /// <summary>A client for the TMS intake API holding the intake role, as the intake Logic App's managed identity does.</summary>
    public HttpClient Intake { get; private set; } = null!;

    public Uri TmsUi { get; private set; } = null!;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);

        // Installs Chromium the first time; CI installs it beforehand with its system dependencies.
        if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
        {
            throw new InvalidOperationException("Installing Chromium for Playwright failed.");
        }

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(
            [$"Parameters:{LocalTokens.SigningKeyParameter}={_tokenSigningKey}"],
            startup.Token);
        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-api", startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("tms-web", startup.Token);

        Intake = _app.CreateHttpClient("tms-api");
        Intake.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueIntakeToken());
        TmsUi = _app.GetEndpoint("tms-web", "http");

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    /// <summary>Submits a Canonical Model booking to TMS intake, as the intake Logic App does, each on a trail of its own.</summary>
    public async Task<HttpResponseMessage> SubmitAsync(JsonObject booking, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
        {
            Content = JsonContent.Create(booking),
        };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        return await Intake.SendAsync(request, cancellationToken);
    }

    // A token as Entra issues it to the intake Logic App's managed identity, signed with the key the TMS trusts locally.
    private string IssueIntakeToken() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.IntakeLogicAppClientId, ["roles"] = new[] { IntakeRole } },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_tokenSigningKey)),
                SecurityAlgorithms.HmacSha256),
        });

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        Intake?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
