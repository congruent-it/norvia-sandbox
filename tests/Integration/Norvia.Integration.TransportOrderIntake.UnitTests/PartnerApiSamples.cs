using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>
/// The sample messages in contracts/partner-api/samples. Each sample booking <c>name.booking.json</c> has its
/// expected Canonical Model Transport Order in <c>name.transport-order.json</c>.
/// </summary>
internal static class PartnerApiSamples
{
    private const string BookingSuffix = ".booking.json";
    private const string BookingsFolder = "PartnerApi/samples/bookings";
    private const string ResponsesFolder = "PartnerApi/samples/responses";

    public static IEnumerable<string> BookingNames() =>
        Contracts.FileNames(BookingsFolder)
            .Where(name => name.EndsWith(BookingSuffix, StringComparison.Ordinal))
            .Select(name => name[..^BookingSuffix.Length])
            .Order(StringComparer.Ordinal);

    public static JsonNode Booking(string name) => Read($"{BookingsFolder}/{name}{BookingSuffix}");

    public static JsonNode ExpectedTransportOrder(string name) => Read($"{BookingsFolder}/{name}.transport-order.json");

    public static IEnumerable<string> ResponseNames() =>
        Contracts.FileNames(ResponsesFolder).Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal);

    public static JsonNode Response(string name) => Read($"{ResponsesFolder}/{name}.json");

    /// <summary>Every way of writing a country that the booking schema allows.</summary>
    public static IEnumerable<string> CountrySpellings() =>
        Read("PartnerApi/booking.v1.schema.json")["$defs"]!["country"]!["enum"]!.AsArray().Select(spelling => spelling!.GetValue<string>());

    private static JsonNode Read(string path) =>
        JsonNode.Parse(Contracts.Read(path)) ?? throw new InvalidOperationException($"{path} holds no JSON.");
}
