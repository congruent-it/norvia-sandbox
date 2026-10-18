using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.Erp.Application.Operators;
using Norvia.Erp.Web.Operators;

namespace Norvia.Erp.Web.UnitTests.Operators;

public sealed class FinanceSignInTests
{
    [Fact]
    public async Task Authenticate_WithALocalUser_SignsInTheDeveloperWithTheRoleTheAppHostGave()
    {
        await using var services = SignIn(Environments.Development, LocalUser("Dev Eloper", ErpRoles.Finance));

        var result = await Request(services).AuthenticateAsync();

        result.Principal!.Identity!.Name.Should().Be("Dev Eloper");
        result.Principal.IsInRole(ErpRoles.Finance).Should().BeTrue();
    }

    [Fact]
    public void AddFinanceSignIn_WithALocalUserOutsideDevelopment_Refuses()
    {
        var starting = () => SignIn(Environments.Production, LocalUser("Dev Eloper", ErpRoles.Finance));

        starting.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
    }

    [Fact]
    public async Task Authenticate_BehindEasyAuth_SignsInTheUserWithTheirAppRoles()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services, ClientPrincipal.Of("Fiona Finance", ErpRoles.Finance));

        var result = await context.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.Name.Should().Be("Fiona Finance");
        result.Principal.IsInRole(ErpRoles.Finance).Should().BeTrue();
    }

    [Fact]
    public async Task Authenticate_BehindEasyAuth_WithoutAName_NamesTheUserByTheirUsername()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services, ClientPrincipal.Encode(new
        {
            auth_typ = EasyAuthHandler.EntraAuthenticationType,
            claims = new[] { new { typ = "preferred_username", val = "fiona@norvia.example" } },
        }));

        var result = await context.AuthenticateAsync();

        result.Principal!.Identity!.Name.Should().Be("fiona@norvia.example");
    }

    [Fact]
    public async Task Authenticate_BehindEasyAuth_WithoutTheHeader_SignsNobodyIn()
    {
        await using var services = SignIn(Environments.Production);

        var result = await Request(services).AuthenticateAsync();

        result.None.Should().BeTrue();
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("bm90IGpzb24=")]
    public async Task Authenticate_BehindEasyAuth_WithAHeaderThatIsNoClientPrincipal_Fails(string header)
    {
        await using var services = SignIn(Environments.Production);

        var result = await Request(services, header).AuthenticateAsync();

        result.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Challenge_BehindEasyAuth_SendsTheBrowserToEntra_AndBackToWhereItWas()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services);
        context.Request.Path = "/rate-cards";
        context.Request.QueryString = new QueryString("?q=RC-1");

        await context.ChallengeAsync();

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/.auth/login/aad?post_login_redirect_uri=%2Frate-cards%3Fq%3DRC-1");
    }

    [Fact]
    public async Task Authenticate_WithALocalUser_DoesNotTrustAnEasyAuthHeader()
    {
        await using var services = SignIn(Environments.Development, LocalUser("Dev Eloper"));
        // Locally there is no Easy Auth in front, so a header like its own is not trusted.
        var context = Request(services, ClientPrincipal.Of("Someone Else", ErpRoles.Finance));

        var result = await context.AuthenticateAsync();

        result.Principal!.Identity!.Name.Should().Be("Dev Eloper");
        result.Principal.IsInRole(ErpRoles.Finance).Should().BeFalse();
    }

    [Fact]
    public async Task Forbid_BehindEasyAuth_TakesTheUserToTheRefusal()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services);

        await context.ForbidAsync();

        context.Response.Headers.Location.ToString().Should().Be("/refused");
    }

    [Fact]
    public async Task Forbid_AUserLackingTheRole_TakesThemToTheRefusal()
    {
        await using var services = SignIn(Environments.Development, LocalUser("Dev Eloper"));
        var context = Request(services);

        await context.ForbidAsync();

        context.Response.Headers.Location.ToString().Should().Be("/refused");
    }

    [Theory]
    [InlineData(ErpRoles.Finance, true)]
    [InlineData("Planner", false)]
    public async Task Authorize_ByTheFinancePolicy_AdmitsOnlyFinance(string role, bool admitted)
    {
        await using var services = SignIn(Environments.Development, LocalUser("Dev Eloper", ErpRoles.Finance));
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, FinanceSignIn.FinancePolicy);

        result.Succeeded.Should().Be(admitted);
    }

    [Fact]
    public async Task Authorize_ByTheFinancePolicy_RefusesSomeoneSignedInWithNoRole()
    {
        await using var services = SignIn(Environments.Production);
        var user = new ClaimsPrincipal(new ClaimsIdentity([], "test"));

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, FinanceSignIn.FinancePolicy);

        result.Succeeded.Should().BeFalse();
    }

    private static Dictionary<string, string?> LocalUser(string name, params string[] roles) =>
        new[] { new KeyValuePair<string, string?>("LocalUser:Name", name) }
            .Concat(roles.Select((role, i) => new KeyValuePair<string, string?>($"LocalUser:Roles:{i}", role)))
            .ToDictionary();

    private static ServiceProvider SignIn(string environment, Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddLogging();

        builder.AddFinanceSignIn();

        return builder.Services.BuildServiceProvider();
    }

    private static DefaultHttpContext Request(IServiceProvider services, string? clientPrincipal = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        if (clientPrincipal is not null)
        {
            context.Request.Headers[EasyAuthHandler.ClientPrincipalHeader] = clientPrincipal;
        }

        return context;
    }
}
