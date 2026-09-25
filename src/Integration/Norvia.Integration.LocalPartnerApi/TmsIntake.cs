using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// Calls the TMS intake API as the intake Logic App does: the Canonical Model Transport Order, the correlation ID, and
/// a token of the one identity holding the intake role. The standard resilience handler retries it, as the workflow's
/// retry policy does in Azure.
/// </summary>
internal sealed partial class TmsIntake(HttpClient http, IOptions<TmsOptions> options, TimeProvider time, ILogger<TmsIntake> logger)
{
    private const string IntakeRole = "TransportOrders.Intake";

    /// <summary>The TMS's status and answer, or 0 and empty when it didn't answer, as the relay takes them.</summary>
    public async Task<(int StatusCode, string Body)> SubmitAsync(string transportOrder, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative))
            {
                Content = new StringContent(transportOrder, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Correlation-ID", correlationId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", IntakeToken());

            using var response = await http.SendAsync(request, cancellationToken);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTmsUnreachable(exception);
            return (0, string.Empty);
        }
    }

    // A token as Entra issues it to the intake Logic App's managed identity.
    private string IntakeToken()
    {
        var tokens = options.Value.Tokens;
        var now = time.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tokens.Issuer,
            Audience = tokens.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["azp"] = tokens.ClientId!, ["roles"] = new[] { IntakeRole } },
            SigningCredentials = new SigningCredentials(tokens.Key, SecurityAlgorithms.HmacSha256),
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The TMS couldn't be reached")]
    private partial void LogTmsUnreachable(Exception exception);
}
