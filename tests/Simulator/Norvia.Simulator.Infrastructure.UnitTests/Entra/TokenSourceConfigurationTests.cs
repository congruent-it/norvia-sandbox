using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Simulator.Infrastructure.DriverApi;
using Norvia.Simulator.Infrastructure.Entra;
using Norvia.Simulator.Infrastructure.ErpApi;
using Norvia.Simulator.Infrastructure.PartnerApi;
using Norvia.Simulator.Infrastructure.TmsApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.Entra;

/// <summary>Where the Simulator's tokens come from is a setting: Entra in Azure, its own signing key locally. Exactly one of the two (ADR 0042).</summary>
public sealed class TokenSourceConfigurationTests : IDisposable
{
    private readonly string _seed = Path.Combine(Path.GetTempPath(), $"norvia-seed-{Guid.NewGuid():N}.json");

    public TokenSourceConfigurationTests() => File.WriteAllText(_seed, Contracts.ReadText("seed/norvia.seed.json"));

    public void Dispose() => File.Delete(_seed);

    private static readonly Dictionary<string, string?> PartnerEntra = new()
    {
        ["PartnerApi:BaseAddress"] = "https://apim.example/partner/v1",
        ["PartnerApi:PersonaClientIds"] = "brouwer-dranken=c0;tuincentrum-groenrijk=c1;van-dijk-transport=c2;kessler-spedition=c3;pakketpunt-express=c4",
        ["PartnerApi:SubscriptionKeys:brouwer-dranken"] = "key",
        ["PartnerApi:SubscriptionKeys:tuincentrum-groenrijk"] = "key",
        ["PartnerApi:SubscriptionKeys:van-dijk-transport"] = "key",
        ["PartnerApi:SubscriptionKeys:kessler-spedition"] = "key",
        ["PartnerApi:SubscriptionKeys:pakketpunt-express"] = "key",
        ["PartnerApi:Entra:TenantId"] = "tenant",
        ["PartnerApi:Entra:Scope"] = "api-client/.default",
        ["PartnerApi:Entra:ManagedIdentityClientId"] = "simulator-client",
    };

    private static readonly Dictionary<string, string?> PartnerLocal = new()
    {
        ["PartnerApi:BaseAddress"] = "https://apim.example/partner/v1",
        ["PartnerApi:PersonaClientIds"] = "brouwer-dranken=c0;tuincentrum-groenrijk=c1;van-dijk-transport=c2;kessler-spedition=c3;pakketpunt-express=c4",
        ["PartnerApi:SubscriptionKeys:brouwer-dranken"] = "key",
        ["PartnerApi:SubscriptionKeys:tuincentrum-groenrijk"] = "key",
        ["PartnerApi:SubscriptionKeys:van-dijk-transport"] = "key",
        ["PartnerApi:SubscriptionKeys:kessler-spedition"] = "key",
        ["PartnerApi:SubscriptionKeys:pakketpunt-express"] = "key",
        ["PartnerApi:LocalTokens:Issuer"] = "issuer",
        ["PartnerApi:LocalTokens:Audience"] = "audience",
        ["PartnerApi:LocalTokens:SigningKey"] = "a2V5",
    };

    private static readonly Dictionary<string, string?> TmsEntra = new()
    {
        ["TmsApi:BaseAddress"] = "https://tms.example",
        ["TmsApi:Entra:Scope"] = "tms-client/.default",
        ["TmsApi:Entra:ManagedIdentityClientId"] = "simulator-client",
    };

    private static readonly Dictionary<string, string?> DriverEntra = new()
    {
        ["DriverApp:BaseAddress"] = "https://driver.example",
        ["DriverApp:Entra:TenantId"] = "tenant",
        ["DriverApp:Entra:Scope"] = "driver-client/.default",
        ["DriverApp:Entra:ManagedIdentityClientId"] = "simulator-client",
        ["DriverApp:Entra:DriverClientIds"] = "DRV-0001=d1",
    };

    private ServiceProvider Services(params Dictionary<string, string?>[] settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:simulator"] = "Server=localhost;Database=simulator;TrustServerCertificate=true",
            ["Seed:Path"] = _seed,
        });
        foreach (var setting in settings)
        {
            builder.Configuration.AddInMemoryCollection(setting);
        }

        builder.AddSimulatorInfrastructure();
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void Tokens_WhenEntraIsConfigured_ComeFromEntra()
    {
        using var services = Services(PartnerEntra, TmsEntra, DriverEntra);

        services.GetRequiredService<IPersonaTokens>().Should().BeOfType<EntraPersonaTokens>();
        services.GetRequiredService<ITmsTokens>().Should().BeOfType<EntraTmsTokens>();
        services.GetRequiredService<IDriverTokens>().Should().BeOfType<EntraDriverTokens>();
    }

    [Fact]
    public void ErpTokens_WhenEntraIsConfigured_ComeFromEntra()
    {
        using var services = Services(PartnerEntra, TmsEntra, DriverEntra, new()
        {
            ["ErpApi:BaseAddress"] = "https://erp.example",
            ["ErpApi:Entra:Scope"] = "erp-client/.default",
            ["ErpApi:Entra:ManagedIdentityClientId"] = "simulator-client",
        });

        services.GetRequiredService<IErpTokens>().Should().BeOfType<EntraErpTokens>();
        services.GetRequiredService<IOptions<ErpApiOptions>>().Value.BaseAddress.Should().Be(new Uri("https://erp.example"));
    }

    [Fact]
    public void ErpTokens_WhenLocalTokensAreConfigured_AreSignedLocally()
    {
        using var services = Services(PartnerEntra, TmsEntra, DriverEntra, new()
        {
            ["ErpApi:BaseAddress"] = "https://erp.example",
            ["ErpApi:LocalTokens:Issuer"] = "https://issuer",
            ["ErpApi:LocalTokens:Audience"] = "api://erp",
            ["ErpApi:LocalTokens:ClientId"] = "simulator",
            ["ErpApi:LocalTokens:SigningKey"] = Convert.ToBase64String(new byte[32]),
        });

        services.GetRequiredService<IErpTokens>().Should().BeOfType<LocalErpTokens>();
    }

    [Fact]
    public void ErpApiOptions_WithNeither_AreInvalid()
    {
        using var services = Services(PartnerEntra, TmsEntra, DriverEntra, new() { ["ErpApi:BaseAddress"] = "https://erp.example" });

        var reading = () => services.GetRequiredService<IOptions<ErpApiOptions>>().Value;

        reading.Should().Throw<OptionsValidationException>().WithMessage("*ErpApi:Entra or ErpApi:LocalTokens*");
    }

    [Fact]
    public void ErpApiOptions_WithAnIncompleteEntra_AreInvalid()
    {
        using var services = Services(PartnerEntra, TmsEntra, DriverEntra, new() { ["ErpApi:BaseAddress"] = "https://erp.example", ["ErpApi:Entra:Scope"] = "erp-client/.default" });

        var reading = () => services.GetRequiredService<IOptions<ErpApiOptions>>().Value;

        reading.Should().Throw<OptionsValidationException>().WithMessage("*ErpApi:Entra needs*");
    }

    [Fact]
    public void PartnerApiOptions_WithBothEntraAndLocalTokens_AreInvalid()
    {
        using var services = Services(PartnerEntra, PartnerLocal, TmsEntra, DriverEntra);

        var reading = () => services.GetRequiredService<IOptions<PartnerApiOptions>>().Value;

        reading.Should().Throw<OptionsValidationException>().WithMessage("*PartnerApi:Entra or PartnerApi:LocalTokens*");
    }

    [Fact]
    public void TmsApiOptions_WithNeither_AreInvalid()
    {
        using var services = Services(PartnerEntra, new() { ["TmsApi:BaseAddress"] = "https://tms.example" }, DriverEntra);

        var reading = () => services.GetRequiredService<IOptions<TmsApiOptions>>().Value;

        reading.Should().Throw<OptionsValidationException>().WithMessage("*TmsApi:Entra or TmsApi:LocalTokens*");
    }

    [Fact]
    public void DriverAppOptions_WithAnIncompleteEntra_AreInvalid()
    {
        using var services = Services(PartnerEntra, TmsEntra, new() { ["DriverApp:BaseAddress"] = "https://driver.example", ["DriverApp:Entra:TenantId"] = "tenant" });

        var reading = () => services.GetRequiredService<IOptions<DriverApiOptions>>().Value;

        reading.Should().Throw<OptionsValidationException>().WithMessage("*DriverApp:Entra needs*");
    }
}
