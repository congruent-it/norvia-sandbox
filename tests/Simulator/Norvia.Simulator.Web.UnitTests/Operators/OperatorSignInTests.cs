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
    public void AddOperatorSignIn_WithoutALocalOperator_RefusesUntilTheAzureSliceAddsEasyAuth()
    {
        var starting = () => SignIn(Environments.Development);

        starting.Should().Throw<InvalidOperationException>().WithMessage("*LocalOperator*");
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

    private static DefaultHttpContext Request(IServiceProvider services) => new() { RequestServices = services };
}
