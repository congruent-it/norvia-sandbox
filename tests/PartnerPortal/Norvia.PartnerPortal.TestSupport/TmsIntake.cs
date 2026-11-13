using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Norvia.AppHost;

namespace Norvia.PartnerPortal.TestSupport;

/// <summary>The TMS API as the intake Logic App calls it: a booking taken in becomes a Transport Order of its Shipper, with the Shipper's own reference.</summary>
public sealed class TmsIntake : IDisposable
{
    private static readonly string[] IntakeRoles = ["TransportOrders.Intake"];
    private readonly HttpClient _http;
    private readonly string _signingKey;

    public TmsIntake(HttpClient http, string signingKey)
    {
        _http = http;
        _signingKey = signingKey;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Takes in a booking for the Shipper with the reference. Returns the Transport Order's number.</summary>
    public async Task<string> EnterTransportOrderAsync(string shipperId, string shipperReference, CancellationToken cancellationToken)
    {
        var collection = DateTimeOffset.UtcNow.Date.AddDays(2);
        var booking = new JsonObject
        {
            ["shipperId"] = shipperId,
            ["shipperReference"] = shipperReference,
            ["collection"] = new JsonObject
            {
                ["address"] = Address("Waalhaven Oostzijde 88", "3087 BM", "Rotterdam"),
                ["window"] = Window(collection.AddHours(6), collection.AddHours(10)),
            },
            ["delivery"] = new JsonObject
            {
                ["address"] = Address("Oudegracht 112", "3511 AW", "Utrecht"),
                ["consignee"] = new JsonObject { ["name"] = "Café De Zwaan", ["contactName"] = "Sanne de Vries", ["phone"] = "+31 30 555 0142", ["email"] = null },
                ["window"] = Window(collection.AddHours(12), collection.AddHours(16)),
            },
            ["goodsLines"] = new JsonArray
            {
                new JsonObject { ["packageType"] = "pallet", ["quantity"] = 2, ["weightKg"] = 850.5, ["dimensionsCm"] = new JsonObject { ["length"] = 120, ["width"] = 80, ["height"] = 150 } },
            },
            ["instructions"] = "Entered for a tracking assistant test",
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/transport-orders", UriKind.Relative)) { Content = JsonContent.Create(booking) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        request.Headers.Add("X-Correlation-ID", Guid.NewGuid().ToString());
        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonNode.Parse(body)!["transportOrderNumber"]!.GetValue<string>();
    }

    private string Token() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = LocalTokens.Issuer,
            Audience = LocalTokens.TmsAudience,
            Expires = DateTime.UtcNow.AddHours(1),
            Claims = new Dictionary<string, object> { ["azp"] = LocalTokens.IntakeLogicAppClientId, ["roles"] = IntakeRoles },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)), SecurityAlgorithms.HmacSha256),
        });

    private static JsonObject Address(string street, string postcode, string city) =>
        new() { ["street"] = street, ["postcode"] = postcode, ["city"] = city, ["country"] = "NL" };

    private static JsonObject Window(DateTimeOffset earliest, DateTimeOffset latest) => new() { ["earliest"] = earliest.ToString("O"), ["latest"] = latest.ToString("O") };
}
