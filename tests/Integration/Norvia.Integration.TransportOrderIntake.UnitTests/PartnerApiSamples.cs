using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake.UnitTests;

/// <summary>
/// The sample messages in contracts/partner-api/samples. Each sample booking <c>name.booking.json</c> has its
/// expected Canonical Model Transport Order in <c>name.transport-order.json</c>.
/// </summary>
internal static class PartnerApiSamples
{
    private const string BookingSuffix = ".booking.json";

    private static readonly string BookingsDirectory = Path.Combine(AppContext.BaseDirectory, "PartnerApi", "samples", "bookings");
    private static readonly string ResponsesDirectory = Path.Combine(AppContext.BaseDirectory, "PartnerApi", "samples", "responses");

    public static IEnumerable<string> BookingNames() =>
        Directory.EnumerateFiles(BookingsDirectory, "*" + BookingSuffix)
            .Select(path => Path.GetFileName(path)[..^BookingSuffix.Length])
            .Order(StringComparer.Ordinal);

    public static JsonNode Booking(string name) => Read(Path.Combine(BookingsDirectory, name + BookingSuffix));

    public static JsonNode ExpectedTransportOrder(string name) => Read(Path.Combine(BookingsDirectory, name + ".transport-order.json"));

    public static IEnumerable<string> ResponseNames() =>
        Directory.EnumerateFiles(ResponsesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.Ordinal);

    public static JsonNode Response(string name) => Read(Path.Combine(ResponsesDirectory, name + ".json"));

    private static JsonNode Read(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidOperationException($"{path} holds no JSON.");
}
