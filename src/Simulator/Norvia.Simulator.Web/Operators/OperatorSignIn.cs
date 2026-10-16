using Microsoft.AspNetCore.Authentication;
using Norvia.Simulator.Application.Operators;

namespace Norvia.Simulator.Web.Operators;

/// <summary>
/// How an operator signs in (ADR 0042, as ADR 0026 for the Integration console). In Azure, Container Apps' Easy Auth signs them
/// in to the Simulator's app registration in Entra ID, and the Simulator reads who they are and their app roles from it. Locally
/// there is no Easy Auth, so the AppHost names a local operator: the developer, with the roles it gives them.
/// </summary>
public static class OperatorSignIn
{
    /// <summary>Configure, preview and start runs, cancel them and clear the history.</summary>
    public const string RunPolicy = OperatorRoles.Run;

    /// <summary>See runs and their logs.</summary>
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
            builder.Services.AddAuthentication(EasyAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, EasyAuthHandler>(EasyAuthHandler.SchemeName, configureOptions: null);
        }

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RunPolicy, policy => policy.RequireRole(OperatorRoles.Run))
            .AddPolicy(ReadPolicy, policy => policy.RequireRole(OperatorRoles.Read));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }
}
