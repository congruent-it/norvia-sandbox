using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Norvia.Simulator.Application.Operators;
using Norvia.Simulator.Web.Operators;

namespace Norvia.Simulator.Web.UnitTests.Operators;

public sealed class OperatorSignInTests
{
    [Fact]
    public async Task Authenticate_WithALocalOperator_SignsInTheDeveloperWithTheRolesTheAppHostGave()
    {
        await using var services = SignIn(Environments.Development, LocalOperator("Dev Eloper", OperatorRoles.Run, OperatorRoles.Read));

        var result = await Request(services).AuthenticateAsync();

        result.Principal!.Identity!.Name.Should().Be("Dev Eloper");
        result.Principal.IsInRole(OperatorRoles.Run).Should().BeTrue();
        result.Principal.IsInRole(OperatorRoles.Read).Should().BeTrue();
    }

    [Fact]
    public void AddOperatorSignIn_WithALocalOperatorOutsideDevelopment_Refuses()
    {
        var starting = () => SignIn(Environments.Production, LocalOperator("Dev Eloper", OperatorRoles.Run));

        starting.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
    }

    [Fact]
    public async Task Authenticate_BehindEasyAuth_SignsInTheOperatorWithTheirAppRoles()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services, ClientPrincipal.Of("Iris Operator", OperatorRoles.Run, OperatorRoles.Read));

        var result = await context.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal!.Identity!.Name.Should().Be("Iris Operator");
        result.Principal.IsInRole(OperatorRoles.Run).Should().BeTrue();
        result.Principal.IsInRole(OperatorRoles.Read).Should().BeTrue();
    }

    [Fact]
    public async Task Authenticate_BehindEasyAuth_WithoutAName_NamesTheOperatorByTheirUsername()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services, ClientPrincipal.Encode(new
        {
            auth_typ = EasyAuthHandler.EntraAuthenticationType,
            claims = new[] { new { typ = "preferred_username", val = "iris@norvia.example" } },
        }));

        var result = await context.AuthenticateAsync();

        result.Principal!.Identity!.Name.Should().Be("iris@norvia.example");
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
        context.Request.Path = "/runs/1";
        context.Request.QueryString = new QueryString("?q=SIM-1");

        await context.ChallengeAsync();

        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/.auth/login/aad?post_login_redirect_uri=%2Fruns%2F1%3Fq%3DSIM-1");
    }

    [Fact]
    public async Task Authenticate_WithALocalOperator_DoesNotTrustAnEasyAuthHeader()
    {
        await using var services = SignIn(Environments.Development, LocalOperator("Dev Eloper", OperatorRoles.Read));
        // Locally there is no Easy Auth in front, so a header like its own is not trusted.
        var context = Request(services, ClientPrincipal.Of("Someone Else", OperatorRoles.Run));

        var result = await context.AuthenticateAsync();

        result.Principal!.Identity!.Name.Should().Be("Dev Eloper");
        result.Principal.IsInRole(OperatorRoles.Run).Should().BeFalse();
    }

    [Fact]
    public async Task Forbid_BehindEasyAuth_TakesTheOperatorToTheRefusal()
    {
        await using var services = SignIn(Environments.Production);
        var context = Request(services);

        await context.ForbidAsync();

        context.Response.Headers.Location.ToString().Should().Be("/refused");
    }

    [Fact]
    public async Task Forbid_AnOperatorLackingARole_TakesThemToTheRefusal()
    {
        await using var services = SignIn(Environments.Development, LocalOperator("Dev Eloper", OperatorRoles.Read));
        var context = Request(services);

        await context.ForbidAsync();

        context.Response.Headers.Location.ToString().Should().Be("/refused");
    }

    [Theory]
    [InlineData(OperatorRoles.Run, OperatorSignIn.RunPolicy, true)]
    [InlineData(OperatorRoles.Read, OperatorSignIn.RunPolicy, false)]
    [InlineData(OperatorRoles.Read, OperatorSignIn.ReadPolicy, true)]
    [InlineData(OperatorRoles.Run, OperatorSignIn.ReadPolicy, false)]
    public async Task Authorize_ByPolicy_AdmitsOnlyTheRoleItNames(string role, string policy, bool admitted)
    {
        await using var services = SignIn(Environments.Development, LocalOperator("Dev Eloper", OperatorRoles.Run));
        var operatorWithRole = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(operatorWithRole, policy);

        result.Succeeded.Should().Be(admitted);
    }

    private static Dictionary<string, string?> LocalOperator(string name, params string[] roles) =>
        new[] { new KeyValuePair<string, string?>("LocalOperator:Name", name) }
            .Concat(roles.Select((role, i) => new KeyValuePair<string, string?>($"LocalOperator:Roles:{i}", role)))
            .ToDictionary();

    private static ServiceProvider SignIn(string environment, Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings ?? []);
        builder.Services.AddLogging();

        builder.AddOperatorSignIn();

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
