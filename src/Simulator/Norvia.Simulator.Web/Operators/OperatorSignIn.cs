using Microsoft.AspNetCore.Authentication;
using Norvia.Simulator.Application.Operators;

namespace Norvia.Simulator.Web.Operators;

/// <summary>
/// How an operator signs in. Locally there is no Easy Auth, so the AppHost names a local operator: the developer, with the
/// roles it gives them. In Azure, Easy Auth signs the presenter in to the Simulator's app registration, which the Azure slice
/// adds (as ADR 0026 does for the Integration console); until then the Simulator refuses to start without a local operator.
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
        if (!localOperator.Exists())
        {
            throw new InvalidOperationException(
                $"The Simulator signs operators in only as a local operator ({LocalOperatorOptions.SectionName}) so far; signing in with Easy Auth in Azure is a later slice.");
        }

        // Anywhere else, a configured operator would let anyone in with the roles it names.
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"A local operator ({LocalOperatorOptions.SectionName}) signs in only in Development.");
        }

        builder.Services.AddOptions<LocalOperatorOptions>().Bind(localOperator).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddAuthentication(LocalOperatorHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, LocalOperatorHandler>(LocalOperatorHandler.SchemeName, configureOptions: null);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(RunPolicy, policy => policy.RequireRole(OperatorRoles.Run))
            .AddPolicy(ReadPolicy, policy => policy.RequireRole(OperatorRoles.Read));
        builder.Services.AddCascadingAuthenticationState();

        return builder;
    }
}
