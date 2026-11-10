using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.BookingDraft.Api;

/// <summary>
/// Who may call the host (ADR 0020 as for the intake Logic App): only API Management. The token is Entra's, for the host's own app registration,
/// and carries its one app role, which only API Management's identity holds. The token's <c>azp</c> must also be the gateway's client ID, so a
/// token from any other client is refused even if it were ever given the role. The Shipper is never read from here: the policy has already
/// taken it from the Shipper's own token and sends it in a header that only this caller may be believed on.
/// </summary>
public static class GatewayAuthentication
{
    public const string Scheme = "Gateway";
    public const string Policy = "Gateway";
    public const string Role = "BookingDrafts.Create";

    public static IHostApplicationBuilder AddGatewayAuthentication(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddAuthentication().AddJwtBearer(Scheme, options => options.MapInboundClaims = false);
        builder.Services.AddOptions<JwtBearerOptions>(Scheme).Configure<IOptions<BookingDraftHostOptions>>((options, host) =>
        {
            var api = host.Value.Api;
            options.Authority = api.Issuer;
            options.TokenValidationParameters.ValidIssuer = api.Issuer;
            options.TokenValidationParameters.ValidAudience = api.Audience;
            options.TokenValidationParameters.RoleClaimType = "roles";
            options.TokenValidationParameters.NameClaimType = "azp";
        });
        builder.Services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireAuthenticatedUser()
            .RequireRole(Role)
            .AddRequirements(new GatewayRequirement()));
        builder.Services.AddSingleton<IAuthorizationHandler, GatewayHandler>();
        return builder;
    }
}
