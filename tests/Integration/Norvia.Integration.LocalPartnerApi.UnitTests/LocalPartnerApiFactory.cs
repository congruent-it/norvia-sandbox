using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalPartnerApi.UnitTests;

/// <summary>
/// Runs the local Partner API in memory, configured as the AppHost configures it, with the TMS replaced by
/// <see cref="Tms"/>. It holds the key both trust, so it issues Persona tokens the way the Partner portal does locally.
/// </summary>
public sealed class LocalPartnerApiFactory : WebApplicationFactory<Program>
{
    public const string BrouwerKey = "brouwer-subscription-key";
    public const string VanDijkKey = "van-dijk-subscription-key";
    public const string BrouwerClientId = "norvia-local-persona-brouwer-dranken";
    public const string VanDijkClientId = "norvia-local-persona-van-dijk-transport";
    public const string Issuer = "https://norvia.local/tokens";
    public const string PartnerApiAudience = "api://norvia-partner-api";
    public const string TmsAudience = "api://norvia-tms";
    public const string IntakeClientId = "norvia-local-intake-logic-app";
    public const string AnswerClientId = "norvia-local-answer-logic-app";

    public LocalPartnerApiFactory() => ProofOfDeliveryQueue = new(InboundProofsOfDelivery);

    public byte[] SigningKey { get; } = RandomNumberGenerator.GetBytes(64);

    public StubTms Tms { get; } = new();

    internal StubTrackingQueue TrackingQueue { get; } = new();

    internal StubInboundProofsOfDelivery InboundProofsOfDelivery { get; } = new();

    internal StubProofOfDeliveryQueue ProofOfDeliveryQueue { get; }

    /// <summary>What the stand-in logged, with its scopes: the tracking keys its traces carry (coding guidelines §7).</summary>
    public FakeLogCollector Logs => Services.GetRequiredService<FakeLogCollector>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var signingKey = Convert.ToBase64String(SigningKey);
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["PartnerApi:SubscriptionKeys:brouwer-dranken"] = BrouwerKey,
            ["PartnerApi:SubscriptionKeys:van-dijk-transport"] = VanDijkKey,
            ["PartnerApi:ShippersByClientId:" + BrouwerClientId] = "SHP-0001",
            ["PartnerApi:CarriersByClientId:" + VanDijkClientId] = "CAR-0001",
            ["PartnerApi:Tokens:Issuer"] = Issuer,
            ["PartnerApi:Tokens:Audience"] = PartnerApiAudience,
            ["PartnerApi:Tokens:SigningKey"] = signingKey,
            ["Tms:BaseAddress"] = "http://tms-api",
            ["Tms:Tokens:Issuer"] = Issuer,
            ["Tms:Tokens:Audience"] = TmsAudience,
            ["Tms:Tokens:ClientId"] = IntakeClientId,
            ["Tms:Tokens:SigningKey"] = signingKey,
            ["Tms:AnswerClientId"] = AnswerClientId,
        })
        {
            builder.UseSetting(name, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddLogging(logging => logging.AddFakeLogging());
            services.AddSingleton<ITrackingQueue>(TrackingQueue);
            services.AddSingleton<IInboundProofsOfDelivery>(InboundProofsOfDelivery);
            services.AddSingleton<IProofOfDeliveryQueue>(ProofOfDeliveryQueue);
            services.AddHttpClient<TmsIntake>().ConfigurePrimaryHttpMessageHandler(() => Tms);
            services.AddHttpClient<TmsCarrierAnswers>().ConfigurePrimaryHttpMessageHandler(() => Tms);
            // Retries straight away, so a test of an unreachable TMS doesn't wait out the backoff.
            services.ConfigureAll<HttpStandardResilienceOptions>(options => options.Retry.Delay = TimeSpan.Zero);
        });
    }

    /// <summary>A Persona's token as the Partner portal issues it locally; change any part to show what is refused.</summary>
    public string PersonaToken(
        string clientId = BrouwerClientId,
        string[]? roles = null,
        string audience = PartnerApiAudience,
        byte[]? signingKey = null,
        DateTime? expires = null)
    {
        var claims = new Dictionary<string, object> { ["azp"] = clientId };
        if ((roles ?? ["TransportOrders.Submit"]) is { Length: > 0 } granted)
        {
            claims["roles"] = granted;
        }

        var validUntil = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            NotBefore = validUntil.AddHours(-2),
            IssuedAt = validUntil.AddHours(-2),
            Expires = validUntil,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? SigningKey), SecurityAlgorithms.HmacSha256),
        });
    }

    public static string Sample(string name) => Embedded($"PartnerApi/samples/bookings/{name}");

    public static string CarrierTrackingSample(string name) => Embedded($"PartnerApi/samples/carrier-tracking/{name}");

    public static string CarrierProofOfDeliverySample(string name) => Embedded($"PartnerApi/samples/carrier-proofs-of-delivery/{name}");

    public static string CarrierAnswerSample(string name) => Embedded($"PartnerApi/samples/carrier-answers/{name}");

    private static string Embedded(string name)
    {
        using var stream = typeof(LocalPartnerApiFactory).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{name} is not an embedded sample.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
