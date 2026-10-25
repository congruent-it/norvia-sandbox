using Microsoft.AspNetCore.Authentication;
using Norvia.HealthCheck.Application.Operators;

namespace Norvia.HealthCheck.Web.Operators;

/// <summary>
/// How an Integration operator signs in. Locally there is no Easy Auth, so the AppHost names a local operator: the developer, with
/// the roles it gives them. Easy Auth in Azure comes with the app's deployment (#392). Where no local operator is configured the app
/// has no way to sign anyone in, so every page refuses: it fails closed.
/// </summary>
public static class OperatorSignIn
{
    /// <summary>Running a health check and seeing what it found.</summary>
    public const string RunPolicy = OperatorRoles.Run;

    public const string ReadPolicy = OperatorRoles.Read;

    public static IHostApplicationBuilder AddOperatorSignIn(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var localOperator = builder.Configuration.GetSection(LocalOperatorOptions.SectionName);
        if (localOperator.Exists())
        {
            // Anywhere else, a configured operator would let anyone in with the roles it names.
            if (!builder.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"A local operator ({LocalOperatorOptions.SectionName}) signs in only in Development; in Azure Easy Auth signs operators in.");
            }

            builder.Services.AddOptions<LocalOperatorOptions>().Bind(localOperator).ValidateDataAnnotations().ValidateOnStart();
            builder.Services.AddAuthentication(LocalOperatorHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, LocalOperatorHandler>(LocalOperatorHandler.SchemeName, configureOptions: null);
        }
        else
        {
            // No scheme: the pipeline still starts, and nobody is signed in.
            builder.Services.AddAuthentication();
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RunPolicy, policy => policy.RequireRole(OperatorRoles.Run))
            .AddPolicy(ReadPolicy, policy => policy.RequireRole(OperatorRoles.Read));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }
}
