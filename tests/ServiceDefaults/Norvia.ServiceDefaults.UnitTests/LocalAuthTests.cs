using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Norvia.ServiceDefaults.UnitTests;

public sealed class LocalAuthTests
{
    private static IConfiguration Configuration(string? allowed) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(allowed is null ? [] : new Dictionary<string, string?> { [LocalAuth.SettingName] = allowed })
            .Build();

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    [Fact]
    public void SettingName_IsLocalAuthAllowed() => LocalAuth.SettingName.Should().Be("LocalAuth:Allowed");

    [Fact]
    public void IsAllowed_IsOffByDefault() => LocalAuth.IsAllowed(Configuration(null)).Should().BeFalse();

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void IsAllowed_ReadsTheSetting(string value, bool expected) =>
        LocalAuth.IsAllowed(Configuration(value)).Should().Be(expected);

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void IsAllowed_RefusesAValueThatIsNotABoolean(string value)
    {
        var act = () => LocalAuth.IsAllowed(Configuration(value));

        act.Should().Throw<InvalidOperationException>().WithMessage("*LocalAuth:Allowed*true or false*");
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Development", "false", true)]
    [InlineData("Production", null, false)]
    [InlineData("Production", "false", false)]
    [InlineData("Production", "true", true)]
    [InlineData("Kubernetes", "true", true)]
    [InlineData("Kubernetes", null, false)]
    public void Permits_IsDevelopmentOrExplicitlyAllowed(string environment, string? allowed, bool expected) =>
        LocalAuth.Permits(Environment(environment), Configuration(allowed)).Should().Be(expected);

    [Fact]
    public void Require_PassesInDevelopment() => LocalAuth.Require(Environment("Development"), Configuration(null), "The local operator");

    [Fact]
    public void Require_PassesWhenAllowed() => LocalAuth.Require(Environment("Production"), Configuration("true"), "The local operator");

    [Fact]
    public void Require_RefusesOutsideDevelopmentUnlessSetExplicitly()
    {
        var act = () => LocalAuth.Require(Environment("Production"), Configuration(null), "The local operator");

        act.Should().Throw<InvalidOperationException>().WithMessage("The local operator*Development*LocalAuth:Allowed*");
    }

    [Fact]
    public void RequireLocalSignIn_RefusesWhileAllowedIfNoLocalSignInIsConfigured()
    {
        // An Easy Auth header is not trusted while LocalAuth:Allowed is on (ADR 0037).
        var act = () => LocalAuth.RequireLocalSignInWhenAllowed(Configuration("true"), localSignInConfigured: false);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Easy Auth*LocalAuth:Allowed*");
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("false", true)]
    public void RequireLocalSignIn_PassesOtherwise(string? allowed, bool configured) =>
        LocalAuth.RequireLocalSignInWhenAllowed(Configuration(allowed), configured);

    private static IConfiguration WithSigningKey() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = "a2V5" })
            .Build();

    [Fact]
    public void RequireForSigningKeys_RefusesAConfiguredLocalKeyOutsideDevelopment()
    {
        var act = () => LocalAuth.RequireForSigningKeys(Environment("Production"), WithSigningKey());

        act.Should().Throw<InvalidOperationException>().WithMessage("*signing key*Development*LocalAuth:Allowed*");
    }

    [Fact]
    public void RequireForSigningKeys_PassesInDevelopment() => LocalAuth.RequireForSigningKeys(Environment("Development"), WithSigningKey());

    [Fact]
    public void RequireForSigningKeys_PassesWhenAllowed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = "a2V5",
                [LocalAuth.SettingName] = "true",
            })
            .Build();

        LocalAuth.RequireForSigningKeys(Environment("Production"), configuration);
    }

    [Fact]
    public void RequireForSigningKeys_PassesWhereNoKeyIsConfigured() =>
        LocalAuth.RequireForSigningKeys(Environment("Production"), Configuration(null));
}
