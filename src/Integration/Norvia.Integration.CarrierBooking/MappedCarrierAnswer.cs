using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking;

/// <summary>
/// What the Carrier-answer map made of a Carrier's answer: the Canonical Model answer for the TMS, or why the answer
/// can't be used. The Carrier booking number is there whenever the answer named a usable one.
/// </summary>
public sealed record MappedCarrierAnswer
{
    private MappedCarrierAnswer()
    {
    }

    public string? CarrierBookingNumber { get; private init; }

    /// <summary>The body of the TMS call; null when the answer can't be used.</summary>
    public JsonObject? TmsRequest { get; private init; }

    /// <summary>Why the answer can't be used, in the Carrier's terms; null when it can.</summary>
    public string? Problem { get; private init; }

    public static MappedCarrierAnswer Mapped(string carrierBookingNumber, JsonObject tmsRequest) =>
        new() { CarrierBookingNumber = carrierBookingNumber, TmsRequest = tmsRequest };

    public static MappedCarrierAnswer Invalid(string? carrierBookingNumber, string problem) =>
        new() { CarrierBookingNumber = carrierBookingNumber, Problem = problem };

    /// <summary>As the Carrier-answer Logic App reads it: <c>carrierBookingNumber</c>, and <c>tmsRequest</c> or <c>problem</c>.</summary>
    public string ToJsonString()
    {
        var mapped = new JsonObject();
        if (CarrierBookingNumber is not null)
        {
            mapped["carrierBookingNumber"] = CarrierBookingNumber;
        }

        if (TmsRequest is not null)
        {
            mapped["tmsRequest"] = TmsRequest.DeepClone();
        }
        else
        {
            mapped["problem"] = Problem;
        }

        return mapped.ToJsonString();
    }

    /// <summary>Reads what <see cref="ToJsonString"/> wrote, as the relay gets it from the workflow.</summary>
    public static MappedCarrierAnswer FromJson(string json)
    {
        var mapped = JsonNode.Parse(json) as JsonObject ?? throw new ArgumentException("The mapped answer isn't a JSON object.", nameof(json));
        var number = (mapped["carrierBookingNumber"] as JsonValue)?.GetValue<string>();
        return mapped["tmsRequest"] is JsonObject request
            ? Mapped(number!, request)
            : Invalid(number, (mapped["problem"] as JsonValue)?.GetValue<string>() ?? "The answer can't be used.");
    }
}
