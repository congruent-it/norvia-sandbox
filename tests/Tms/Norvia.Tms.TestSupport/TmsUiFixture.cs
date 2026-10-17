using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using Norvia.AppHost;

namespace Norvia.Tms.TestSupport;

/// <summary>
/// Runs the Norvia AppHost once for the whole test run (TMS API, TMS UI, SQL and the Service Bus emulator) and a
/// headless Chromium to drive the TMS UI. It holds the key the TMS trusts locally, so <see cref="Intake"/> submits
/// Transport Orders the way the intake Logic App does.
/// </summary>
public sealed class TmsUiFixture : IAsyncLifetime
{
    private const string IntakeRole = "TransportOrders.Intake";
    private const string AnswerRole = "CarrierBookings.Answer";
    private const string ManageRole = "TransportOrders.Manage";
    private const string TrackingRecordRole = "TrackingEvents.Record";
    private const string ProofOfDeliveryRecordRole = "ProofsOfDelivery.Record";
    private const string ProofOfDeliveryLogicAppClientId = "norvia-local-proof-of-delivery-logic-app";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);
    private readonly string _tokenSigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private DistributedApplication? _app;
    private IPlaywright? _playwright;

    /// <summary>A client for the TMS intake API holding the intake role, as the intake Logic App's managed identity does.</summary>
    public HttpClient Intake { get; private set; } = null!;

    /// <summary>A client for the TMS API holding the answer role, as the Carrier-answer Logic App's managed identity does.</summary>
    public HttpClient Answer { get; private set; } = null!;

    /// <summary>A client for the TMS API holding the record role, as the Carrier-tracking Logic App's managed identity does.</summary>
    public HttpClient TrackingRecord { get; private set; } = null!;

    /// <summary>A client for the TMS API holding the Proof of delivery record role, as the Carrier Proof of delivery Logic App's managed identity does.</summary>
    public HttpClient ProofOfDeliveryRecord { get; private set; } = null!;

    /// <summary>A client for the TMS API holding the manage role, as the TMS UI's managed identity does for the Planner's actions.</summary>
    public HttpClient Manage { get; private set; } = null!;

    public Uri TmsUi { get; private set; } = null!;

    /// <summary>The Partner portal, where a Carrier Persona's inbox shows the booking requests the booking worker sent it.</summary>
    public Uri PartnerPortal { get; private set; } = null!;

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
        // A released Shipment's booking goes through the worker that plays the P4 booking Logic App (ADR 0030) to the portal.
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("partner-portal-web", startup.Token);
        await _app.ResourceNotifications.WaitForResourceAsync("booking-worker", KnownResourceStates.Running, startup.Token);
        // A Shipper notification goes through the worker that plays the P7 Shipper notification Logic App (ADR 0033) to the portal.
        await _app.ResourceNotifications.WaitForResourceAsync("shipper-notification-worker", KnownResourceStates.Running, startup.Token);
        // A cancelled or withdrawn Carrier booking goes through the worker that plays the P7 Carrier cancellation Logic App (ADR 0034) to the portal.
        await _app.ResourceNotifications.WaitForResourceAsync("carrier-cancellation-worker", KnownResourceStates.Running, startup.Token);
        // A dispatched Trip goes through the worker that plays the P11 Trip dispatch Logic App to the Driver app.
        await _app.ResourceNotifications.WaitForResourceAsync("trip-dispatch-worker", KnownResourceStates.Running, startup.Token);
        // A Carrier's answer goes through the stand-in that plays API Management and the Carrier-answer Logic App (ADR 0023).
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("partner-api", startup.Token);

        Intake = _app.CreateHttpClient("tms-api");
        Intake.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(IntakeRole, LocalTokens.IntakeLogicAppClientId));
        Answer = _app.CreateHttpClient("tms-api");
        Answer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(AnswerRole, LocalTokens.AnswerLogicAppClientId));
        Manage = _app.CreateHttpClient("tms-api");
        Manage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(ManageRole, LocalTokens.TmsUiClientId));
        TrackingRecord = _app.CreateHttpClient("tms-api");
        TrackingRecord.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(TrackingRecordRole, LocalTokens.TrackingLogicAppClientId));
        ProofOfDeliveryRecord = _app.CreateHttpClient("tms-api");
        ProofOfDeliveryRecord.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(ProofOfDeliveryRecordRole, ProofOfDeliveryLogicAppClientId));
        TmsUi = _app.GetEndpoint("tms-web", "http");
        PartnerPortal = _app.GetEndpoint("partner-portal-web", "http");

        // The TMS seeds the demo's Trip day in the background and tries again every 30 seconds if the first look fails, so it can
        // be healthy before its Shipments are on the list. The Trip journeys build on them.
        await WaitForDemoShipmentsAsync(startup.Token);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync();
    }

    private async Task WaitForDemoShipmentsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var response = await Manage.GetAsync(new Uri("/v1/shipments/to-put-on-trips", UriKind.Relative), cancellationToken);
            if (response.IsSuccessStatusCode && (await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken)) is { Count: > 0 })
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
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

    /// <summary>Confirms a Carrier booking, as the Carrier-answer Logic App does for a Carrier's answer, each on a trail of its own.</summary>
    public async Task<HttpResponseMessage> ConfirmAsync(string carrierId, string carrierBookingNumber, string carrierBookingReference, CancellationToken cancellationToken)
    {
        var answer = new JsonObject { ["carrierId"] = carrierId, ["outcome"] = "Confirmed", ["carrierBookingReference"] = carrierBookingReference };
        return await PostAsync(Answer, $"/v1/carrier-bookings/{carrierBookingNumber}/answer", answer, cancellationToken);
    }

    /// <summary>Notifies the Shipper of an open Exception, as the Planner does through the TMS UI, on a trail of its own.</summary>
    public async Task<HttpResponseMessage> NotifyShipperAsync(string exceptionNumber, string message, CancellationToken cancellationToken) =>
        await PostAsync(Manage, $"/v1/exceptions/{exceptionNumber}/notify-shipper", new JsonObject { ["message"] = message, ["planner"] = "Anna de Vries" }, cancellationToken);

    /// <summary>The number of the Open Exception of a Shipment, once the TMS has opened it.</summary>
    public async Task<string?> OpenExceptionOfAsync(string shipmentNumber, CancellationToken cancellationToken)
    {
        using var response = await Manage.GetAsync(new Uri("/v1/exceptions/open", UriKind.Relative), cancellationToken);
        response.EnsureSuccessStatusCode();
        var open = await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken);
        return open?.FirstOrDefault(exception => exception?["shipmentNumber"]?.GetValue<string>() == shipmentNumber)?["exceptionNumber"]?.GetValue<string>();
    }

    /// <summary>Records a Canonical Model Tracking event, as the Carrier-tracking Logic App does, each on a trail of its own.</summary>
    public async Task<HttpResponseMessage> RecordTrackingAsync(JsonObject trackingEvent, CancellationToken cancellationToken) =>
        await PostAsync(TrackingRecord, "/v1/tracking-events", trackingEvent, cancellationToken);

    /// <summary>Records a Canonical Model Proof of delivery with one file, as the Carrier Proof of delivery Logic App does, on a trail of its own.</summary>
    public async Task<HttpResponseMessage> RecordProofOfDeliveryAsync(JsonObject metadata, string fileName, byte[] file, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent { { new StringContent(metadata.ToJsonString()), "metadata" } };
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        form.Add(content, "files", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/proofs-of-delivery", UriKind.Relative)) { Content = form };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        return await ProofOfDeliveryRecord.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient caller, string path, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        return await caller.SendAsync(request, cancellationToken);
    }

    // A token as Entra issues it to a Logic App's managed identity, signed with the key the TMS trusts locally.
    private string IssueToken(string role, string clientId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = new[] { role } },
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
        Answer?.Dispose();
        Manage?.Dispose();
        TrackingRecord?.Dispose();
        ProofOfDeliveryRecord?.Dispose();

        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
