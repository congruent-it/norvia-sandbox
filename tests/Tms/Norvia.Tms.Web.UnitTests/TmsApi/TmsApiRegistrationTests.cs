using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Norvia.Tms.Web.TmsApi;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

/// <summary>How the TMS UI is wired to call the TMS API, locally and in Azure.</summary>
public sealed class TmsApiRegistrationTests
{
    private static readonly Dictionary<string, string?> Local = new()
    {
        ["TmsApi:BaseAddress"] = "http://tms-api",
        ["TmsApi:LocalTokens:Issuer"] = "https://norvia.local/tokens",
        ["TmsApi:LocalTokens:Audience"] = "api://norvia-tms",
        ["TmsApi:LocalTokens:ClientId"] = "norvia-local-tms-ui",
        ["TmsApi:LocalTokens:SigningKey"] = Convert.ToBase64String(new byte[64]),
    };

    private static readonly Dictionary<string, string?> Entra = new(Local.Where(setting => !setting.Key.StartsWith("TmsApi:LocalTokens", StringComparison.Ordinal)))
    {
        ["TmsApi:Entra:Scope"] = "00000000-0000-0000-0000-000000000002/.default",
        ["TmsApi:Entra:ManagedIdentityClientId"] = "00000000-0000-0000-0000-000000000003",
    };

    [Fact]
    public void Locally_TheTmsUiSignsItsTokenItself()
    {
        using var services = Build(Local);

        services.GetRequiredService<ITmsApiTokens>().Should().BeOfType<LocalTmsApiTokens>();
    }

    [Fact]
    public void InAzure_TheTmsUisTokenComesFromEntra()
    {
        using var services = Build(Entra);

        services.GetRequiredService<ITmsApiTokens>().Should().BeOfType<EntraTmsApiTokens>();
    }

    [Fact]
    public void TheCommands_GoToTheTmsApi()
    {
        using var services = Build(Local);

        services.GetRequiredService<ITransportOrderCommands>().Should().BeOfType<TmsApiClient>();
    }

    [Fact]
    public void BothOrNeitherWayOfGettingTokens_FailsValidation()
    {
        using var both = Build(new Dictionary<string, string?>(Local.Concat(Entra.Where(setting => setting.Key.StartsWith("TmsApi:Entra", StringComparison.Ordinal)))));
        using var neither = Build(new Dictionary<string, string?>(Local.Where(setting => !setting.Key.StartsWith("TmsApi:LocalTokens", StringComparison.Ordinal))));

        foreach (var services in new[] { both, neither })
        {
            var read = () => services.GetRequiredService<IOptions<TmsApiOptions>>().Value;
            read.Should().Throw<OptionsValidationException>().WithMessage("*either TmsApi:Entra or TmsApi:LocalTokens*");
        }
    }

    [Theory]
    [InlineData("TmsApi:LocalTokens:ClientId", "*TmsApi:LocalTokens needs*")]
    [InlineData("TmsApi:Entra:Scope", "*TmsApi:Entra needs*")]
    [InlineData("TmsApi:BaseAddress", "*BaseAddress*")]
    public void IncompleteSettings_FailValidation(string missing, string message)
    {
        var settings = missing.StartsWith("TmsApi:Entra", StringComparison.Ordinal) ? Entra : Local;
        using var services = Build(new Dictionary<string, string?>(settings.Where(setting => setting.Key != missing)));

        var read = () => services.GetRequiredService<IOptions<TmsApiOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage(message);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddTmsApi();
        return builder.Services.BuildServiceProvider();
    }
}
