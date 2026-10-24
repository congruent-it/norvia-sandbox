using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Norvia.IntegrationConsole.Application.Agents;

namespace Norvia.IntegrationConsole.Web.Agents;

/// <summary>
/// How the agent API checks who calls it (ADR 0040): a bearer token of its own scheme, never the operators' sign-in, and the
/// one read role. The operators' roles open nothing here, and an Agent's token opens nothing else in the console.
/// </summary>
public static class AgentApiAuthentication
{
    public const string Scheme = "AgentToken";

    public const string Policy = AgentRoles.Read;

    /// <summary>Whether the Agents are on (<c>Agents:Enabled</c>, off unless the AppHost or the estate turns it on).</summary>
    public static bool IsEnabled(IConfiguration configuration) => configuration.GetValue<bool>($"{AgentsOptions.SectionName}:{nameof(AgentsOptions.Enabled)}");

    public static IHostApplicationBuilder AddAgentApi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (!IsEnabled(builder.Configuration))
        {
            return builder;
        }

        builder.Services.AddOptions<AgentApiOptions>().BindConfiguration(AgentApiOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddAuthentication()
            .AddJwtBearer(Scheme, options => options.MapInboundClaims = false);
        builder.Services.AddOptions<JwtBearerOptions>(Scheme)
            .Configure<Microsoft.Extensions.Options.IOptions<AgentApiOptions>>((options, api) =>
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = api.Value.Issuer,
                    ValidAudience = api.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(api.Value.SigningKey)),
                    RoleClaimType = "roles",
                    NameClaimType = "azp",
                });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireRole(AgentRoles.Read));

        return builder;
    }
}
