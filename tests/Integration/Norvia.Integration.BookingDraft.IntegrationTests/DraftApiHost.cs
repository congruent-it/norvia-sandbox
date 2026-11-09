using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.Integration.BookingDraft;

namespace Norvia.Integration.BookingDraft.IntegrationTests;

/// <summary>
/// The local Partner API as the AppHost runs it for the Shipper Personas: the real <c>Program</c>, with the Intake agent switched on, one Shipper
/// Persona and a subscription key of its own, so a test never shares a rate limit counter with another. Service Bus and Blob storage are the Partner
/// API's other dependencies, which a Booking draft never reaches. A test may swap the model the Intake agent asks, to show a reply the scripted
/// model never gives. It records the logs (with their scopes) and the spans the host writes, to show that no text or draft reaches the telemetry.
/// </summary>
public sealed class DraftApiHost : WebApplicationFactory<Program>
{
    public const string DraftRole = "TransportOrders.Draft";
    public const string ShipperClientId = "norvia-local-persona-brouwer-dranken";
    public const string ShipperId = "SHP-0001";

    private const string Issuer = "https://norvia.local/tokens";
    private const string Audience = "api://norvia-partner-api";

    private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(64);
    private readonly List<string> _spans = [];
    private readonly ActivityListener _listener;
    private readonly Func<IChatClient>? _model;

    public DraftApiHost(int draftsPerMinute = 10, Func<IChatClient>? model = null)
    {
        DraftsPerMinute = draftsPerMinute;
        _model = model;
        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_spans)
                {
                    _spans.Add(activity.DisplayName + " " + string.Join(' ', activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))
                        + " " + string.Join(' ', activity.Events.SelectMany(e => e.Tags).Select(tag => $"{tag.Key}={tag.Value}")));
                }
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>This host's subscription key for the Norvia partners product.</summary>
    public string SubscriptionKey { get; } = "test-key-" + Guid.NewGuid().ToString("N");

    public int DraftsPerMinute { get; }

    /// <summary>Everything the host logged and every span it recorded, as one text a test can search.</summary>
    public string Telemetry
    {
        get
        {
            var logs = Services.GetRequiredService<FakeLogCollector>().GetSnapshot()
                .Select(log => log.Message + " " + log.Exception + " " + string.Join(' ', log.Scopes.Select(ScopeText)));
            lock (_spans)
            {
                return string.Join('\n', logs.Concat(_spans));
            }
        }
    }

    /// <summary>Posts <paramref name="text"/> to the Booking draft operation as the Shipper Persona; change any part to show what is refused.</summary>
    public async Task<HttpResponseMessage> DraftAsync(
        string text, string? subscriptionKey = null, string? token = null, bool withoutSubscriptionKey = false, bool withoutToken = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/partner/v1/booking-drafts")
        {
            Content = new StringContent(new JsonObject { ["text"] = text }.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!withoutSubscriptionKey)
        {
            request.Headers.Add("Ocp-Apim-Subscription-Key", subscriptionKey ?? SubscriptionKey);
        }

        if (!withoutToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? PersonaToken());
        }

        using var client = CreateClient();
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A Persona's token as the Partner portal issues it locally.</summary>
    public string PersonaToken(string clientId = ShipperClientId, string[]? roles = null, byte[]? signingKey = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = clientId, ["roles"] = roles ?? [DraftRole] },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? _signingKey), SecurityAlgorithms.HmacSha256),
        });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:messaging", "Endpoint=sb://unused.servicebus.windows.net/;SharedAccessKeyName=unused;SharedAccessKey=dW51c2Vk");
        builder.UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true");
        var key = Convert.ToBase64String(_signingKey);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["PartnerApi:SubscriptionKeys:brouwer-dranken"] = SubscriptionKey,
            ["PartnerApi:ShippersByClientId:" + ShipperClientId] = ShipperId,
            ["PartnerApi:Tokens:Issuer"] = Issuer,
            ["PartnerApi:Tokens:Audience"] = Audience,
            ["PartnerApi:Tokens:SigningKey"] = key,
            ["Tms:BaseAddress"] = "http://tms-api",
            ["Tms:Tokens:Issuer"] = Issuer,
            ["Tms:Tokens:Audience"] = "api://norvia-tms",
            ["Tms:Tokens:ClientId"] = "norvia-local-intake-logic-app",
            ["Tms:Tokens:SigningKey"] = key,
            ["Tms:AnswerClientId"] = "norvia-local-answer-logic-app",
            ["Tms:ShipperTrackingClientId"] = "norvia-local-api-management",
            ["CarrierInvoice:BaseAddress"] = "http://carrier-invoice-worker",
            ["CarrierInvoice:Tokens:Issuer"] = Issuer,
            ["CarrierInvoice:Tokens:Audience"] = "api://norvia-carrier-invoice-logic-app",
            ["CarrierInvoice:Tokens:ClientId"] = "norvia-local-api-management",
            ["CarrierInvoice:Tokens:SigningKey"] = key,
            ["Agents:Enabled"] = "true",
            ["Agents:DraftsPerMinute"] = DraftsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
        })
        {
            builder.UseSetting(name, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(logging => logging.AddFakeLogging());
            if (_model is not null)
            {
                services.AddSingleton(new BookingDraftExtractor(_model(), TimeSpan.FromSeconds(5)));
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _listener.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string ScopeText(object? scope) =>
        scope is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : scope?.ToString() ?? string.Empty;
}
