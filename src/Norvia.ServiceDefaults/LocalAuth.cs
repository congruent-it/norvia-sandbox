using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Norvia.ServiceDefaults;

/// <summary>
/// The one named opt-in for the local sign-in (ADR 0026, ADR 0037) and the local token-signing key outside Development, such as on
/// Kubernetes (ADR 0053), where there is no identity provider. It is independent of the environment name and off by default: nothing
/// outside Development runs the local sign-in or trusts the local key unless <c>LocalAuth:Allowed</c> is explicitly <c>true</c>.
/// Setting it states that Norvia's ingress is not reachable from the public internet and no other namespace can reach its pods.
/// </summary>
public static class LocalAuth
{
    public const string SettingName = "LocalAuth:Allowed";

    private const string SigningKeysSection = "Authentication:Schemes:Bearer:SigningKeys";

    /// <summary>Whether the setting is on. Absent is off; a value that is not <c>true</c> or <c>false</c> is refused, not read as off.</summary>
    public static bool IsAllowed(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[SettingName];
        if (value is null)
        {
            return false;
        }

        return bool.TryParse(value, out var allowed)
            ? allowed
            : throw new InvalidOperationException($"{SettingName} must be true or false, not '{value}'.");
    }

    /// <summary>Whether local sign-in and the local key may run: in Development, or when the setting is on.</summary>
    public static bool Permits(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.IsDevelopment() || IsAllowed(configuration);
    }

    /// <summary>Refuses to start unless <see cref="Permits"/>. <paramref name="what"/> names the thing that is refused, as the start of a sentence.</summary>
    public static void Require(IHostEnvironment environment, IConfiguration configuration, string what)
    {
        if (!Permits(environment, configuration))
        {
            throw new InvalidOperationException(
                $"{what} runs only in Development, or where {SettingName} is set to true; elsewhere an identity provider (Easy Auth, Entra) signs in.");
        }
    }

    /// <summary>
    /// An Easy Auth header is not trusted while the setting is on, since anyone who reaches the app could send one. So with it on, the local
    /// sign-in must be what signs users in; an app that configures none refuses to start.
    /// </summary>
    public static void RequireLocalSignInWhenAllowed(IConfiguration configuration, bool localSignInConfigured)
    {
        if (!localSignInConfigured && IsAllowed(configuration))
        {
            throw new InvalidOperationException(
                $"{SettingName} is on, so an Easy Auth header is not trusted, but no local sign-in is configured. Configure the local sign-in or turn {SettingName} off.");
        }
    }

    /// <summary>Refuses to start when the API is given a local token-signing key outside Development without the setting.</summary>
    public static void RequireForSigningKeys(IHostEnvironment environment, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.GetSection(SigningKeysSection).GetChildren().Any())
        {
            Require(environment, configuration, "The local token signing key (Authentication:Schemes:Bearer:SigningKeys)");
        }
    }
}
