using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Erp.Api.Agents;

/// <summary>
/// How the agent view checks who calls it (ADR 0040): a bearer token of its own scheme, never the scheme the other ERP endpoints take.
/// A token for the agent view opens nothing else in the ERP, and the ERP's other tokens open nothing here: the view takes the audience
/// of its own app registration and the one read role.
/// </summary>
public static class AgentApiAuthentication
{
    public const string Scheme = "AgentToken";

    public static IHostApplicationBuilder AddAgentApi(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<AgentApiOptions>().BindConfiguration(AgentApiOptions.SectionName);
        builder.Services.AddAuthentication()
            .AddJwtBearer(Scheme, options => options.MapInboundClaims = false);
        builder.Services.AddOptions<JwtBearerOptions>(Scheme)
            .Configure<IOptions<AgentApiOptions>>((options, api) =>
            {
                var settings = api.Value;
                // Locally the AppHost's key signs the tokens. In Azure there is none, and Entra's issuer gives the keys. An ERP that is
                // told neither trusts no one: a token has no key to be checked against.
                var signingKey = string.IsNullOrWhiteSpace(settings.SigningKey) ? null : new SymmetricSecurityKey(Convert.FromBase64String(settings.SigningKey));
                if (signingKey is null && !string.IsNullOrWhiteSpace(settings.Issuer))
                {
                    options.Authority = settings.Issuer;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = settings.Issuer,
                    ValidAudience = settings.Audience,
                    IssuerSigningKey = signingKey,
                    RoleClaimType = "roles",
                    NameClaimType = "azp",
                };
            });
        return builder;
    }
}
