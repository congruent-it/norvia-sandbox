using System.Text.Json.Nodes;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>Changes a test makes to a copy of the seed file before it starts a Simulator on it.</summary>
internal static class SeedEdits
{
    /// <summary>Only the given Carriers are active, so the Planner can be sent to one the TMS knows as inactive.</summary>
    public static Action<JsonObject> OnlyCarriers(params string[] carrierIds) => seed =>
    {
        foreach (var carrier in seed["carriers"]!.AsArray().OfType<JsonObject>())
        {
            carrier["active"] = carrierIds.Contains(carrier["id"]!.GetValue<string>());
        }
    };

    /// <summary>
    /// Only the given Shippers are active, so the Simulator books only their Lanes. The seed's Carrier Rate Cards cover every Lane of SHP-0001
    /// and none of SHP-0003's, which decides whether the TMS can price a release.
    /// </summary>
    public static Action<JsonObject> OnlyShippers(params string[] shipperIds) => seed =>
    {
        foreach (var shipper in seed["shippers"]!.AsArray().OfType<JsonObject>())
        {
            shipper["active"] = shipperIds.Contains(shipper["id"]!.GetValue<string>());
        }
    };

    /// <summary>The given Carriers have no Persona, so the Simulator cannot play them.</summary>
    public static Action<JsonObject> WithoutPersonasOf(params string[] carrierIds) => seed =>
    {
        var personas = seed["personas"]!.AsArray();
        foreach (var persona in personas.OfType<JsonObject>()
            .Where(persona => persona["party"]!["type"]!.GetValue<string>() == "Carrier" && carrierIds.Contains(persona["party"]!["id"]!.GetValue<string>()))
            .ToList())
        {
            personas.Remove(persona);
        }
    };
}
