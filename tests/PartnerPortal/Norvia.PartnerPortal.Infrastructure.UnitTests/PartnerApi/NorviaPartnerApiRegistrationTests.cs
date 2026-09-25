using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Infrastructure.PartnerApi;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.PartnerApi;

/// <summary>How the portal is wired to call the Norvia Partner API, locally and in Azure.</summary>
public sealed class NorviaPartnerApiRegistrationTests
{
    private static readonly Dictionary<string, string?> Local = new()
    {
        ["PartnerApi:BaseAddress"] = "http://partner-api/partner/v1",
        ["PartnerApi:PersonaClientIds"] = "brouwer-dranken=client-1;van-dijk-transport=client-2",
        ["PartnerApi:SubscriptionKeys:brouwer-dranken"] = "key-1",
        ["PartnerApi:SubscriptionKeys:van-dijk-transport"] = "key-2",
        ["PartnerApi:LocalTokens:Issuer"] = "https://norvia.local/tokens",
        ["PartnerApi:LocalTokens:Audience"] = "api://norvia-partner-api",
        ["PartnerApi:LocalTokens:SigningKey"] = Convert.ToBase64String(new byte[64]),
    };

    private static readonly Dictionary<string, string?> Entra = new(Local.Where(setting => !setting.Key.StartsWith("PartnerApi:LocalTokens", StringComparison.Ordinal)))
    {
        ["PartnerApi:Entra:TenantId"] = "00000000-0000-0000-0000-000000000001",
        ["PartnerApi:Entra:Scope"] = "00000000-0000-0000-0000-000000000002/.default",
        ["PartnerApi:Entra:ManagedIdentityClientId"] = "00000000-0000-0000-0000-000000000003",
    };

    [Fact]
    public void Locally_ThePortalSignsThePersonaTokensItself()
    {
        using var services = Build(Local);

        services.GetRequiredService<IPersonaTokens>().Should().BeOfType<LocalPersonaTokens>();
    }

    [Fact]
    public void InAzure_ThePersonaTokensComeFromEntra()
    {
        using var services = Build(Entra);

        services.GetRequiredService<IPersonaTokens>().Should().BeOfType<EntraPersonaTokens>();
    }

    [Fact]
    public void BothOrNeitherWayOfGettingTokens_FailsValidation()
    {
        using var both = Build(new Dictionary<string, string?>(Local.Concat(Entra.Where(setting => setting.Key.StartsWith("PartnerApi:Entra", StringComparison.Ordinal)))));
        using var neither = Build(new Dictionary<string, string?>(Local.Where(setting => !setting.Key.StartsWith("PartnerApi:LocalTokens", StringComparison.Ordinal))));

        foreach (var services in new[] { both, neither })
        {
            var read = () => services.GetRequiredService<IOptions<PartnerApiOptions>>().Value;
            read.Should().Throw<OptionsValidationException>().WithMessage("*either PartnerApi:Entra or PartnerApi:LocalTokens*");
        }
    }

    [Theory]
    [InlineData("PartnerApi:LocalTokens:SigningKey", "*PartnerApi:LocalTokens needs*")]
    [InlineData("PartnerApi:Entra:TenantId", "*PartnerApi:Entra needs*")]
    public void IncompleteTokenSettings_FailValidation(string missing, string message)
    {
        var settings = missing.StartsWith("PartnerApi:Entra", StringComparison.Ordinal) ? Entra : Local;
        using var services = Build(new Dictionary<string, string?>(settings.Where(setting => setting.Key != missing)));

        var read = () => services.GetRequiredService<IOptions<PartnerApiOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage(message);
    }

    [Fact]
    public async Task TheClient_PostsUnderTheBaseAddress_EvenWithoutATrailingSlash()
    {
        using var partnerApi = new StubHandler();
        partnerApi.Answer(HttpStatusCode.Created, Contracts.Read("PartnerApi/samples/responses/accepted.json"));
        using var services = Build(Local, builder => builder.Services.AddHttpClient<INorviaPartnerApi, NorviaPartnerApiClient>()
            .ConfigurePrimaryHttpMessageHandler(() => partnerApi));

        await services.GetRequiredService<INorviaPartnerApi>().SubmitAsync(Samples.BrouwerDranken, new BookingRequest(), TestContext.Current.CancellationToken);

        partnerApi.Request!.RequestUri.Should().Be(new Uri("http://partner-api/partner/v1/transport-orders"));
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings, Action<HostApplicationBuilder>? more = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        var personas = Substitute.For<IPersonaDirectory>();
        personas.All.Returns([Samples.BrouwerDranken, Samples.VanDijkTransport]);
        builder.Services.AddSingleton(personas);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLogging();
        builder.AddNorviaPartnerApi();
        more?.Invoke(builder);
        return builder.Services.BuildServiceProvider();
    }
}
