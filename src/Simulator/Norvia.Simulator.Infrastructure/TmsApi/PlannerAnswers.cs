using System.Text.Json;
using System.Text.Json.Nodes;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Domain.Planner;

namespace Norvia.Simulator.Infrastructure.TmsApi;

/// <summary>
/// Reads the TMS API's answers to the Planner's calls (the TMS API's OpenAPI document): an assignment or reassignment that
/// went through, a refusal with its code, and the Own fleet's open days. An answer that says neither is not received.
/// </summary>
internal static class PlannerAnswers
{
    public static PlannerAnswer Assigned(int statusCode, string body, string? correlationHeader) => Read(statusCode, body, correlationHeader, LogOutcome.Planned, shipmentNumber: null, [200, 201]);

    public static PlannerAnswer Reassigned(int statusCode, string body, string? correlationHeader, string shipmentNumber) => Read(statusCode, body, correlationHeader, LogOutcome.Reassigned, shipmentNumber, [200]);

    public static PlannerAnswer Released(int statusCode, string body, string? correlationHeader) => Read(statusCode, body, correlationHeader, LogOutcome.Released, shipmentNumber: null, [200]);

    public static PlannerAnswer Cancelled(int statusCode, string body, string? correlationHeader) => Read(statusCode, body, correlationHeader, LogOutcome.BookingCancelled, shipmentNumber: null, [200]);

    public static PlannerAnswer Resolved(int statusCode, string body, string? correlationHeader) => Read(statusCode, body, correlationHeader, LogOutcome.ExceptionResolved, shipmentNumber: null, [200]);

    /// <summary>The answer to creating a Draft Trip: 201 with the Trip's number. Creating one publishes nothing, so there is no correlation ID.</summary>
    public static PlannerAnswer TripCreated(int statusCode, string body, string? correlationHeader) =>
        Read(statusCode, body, correlationHeader, LogOutcome.TripBuilt, shipmentNumber: null, [201], readTripNumber: true);

    /// <summary>The answer to dispatching a Trip: 200 with the correlation ID of the Dispatch, which finds the Trip's Message trail.</summary>
    public static PlannerAnswer TripDispatched(int statusCode, string body, string? correlationHeader, string tripNumber) =>
        Read(statusCode, body, correlationHeader, LogOutcome.TripDispatched, shipmentNumber: null, [200], readTripNumber: true, tripNumber);

    public static PlannerAnswer NotReceived(string detail) => new(LogOutcome.PlanningNotReceived, null, null, detail);

    public static IReadOnlyList<FleetDay>? Days(string body)
    {
        if (Parse(body) is not JsonArray days)
        {
            return null;
        }

        var result = new List<FleetDay>(days.Count);
        foreach (var day in days.OfType<JsonObject>())
        {
            if (day["date"]?.GetValue<string>() is not { } date
                || !DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
                || day["load"] is not JsonObject load)
            {
                return null;
            }

            result.Add(new FleetDay(
                parsed, Number(load, "capacityKg"), (int)Number(load, "capacityPalletPlaces"), Number(load, "demandKg"), (int)Number(load, "demandPalletPlaces"))
            {
                Shifts = Shifts(day["shifts"]),
            });
        }

        return result;
    }

    // The Drivers who work the day. A Shift the Planner can't read is left out: the TMS refuses a Trip that needs it.
    private static List<DriverShift> Shifts(JsonNode? shifts) =>
    [
        .. (shifts as JsonArray)?.OfType<JsonObject>()
            .Select(shift => (Driver: Text(shift, "driverId"), Start: Time(shift, "start"), End: Time(shift, "end")))
            .Where(shift => shift.Driver is not null && shift.Start is not null && shift.End is not null)
            .Select(shift => new DriverShift(shift.Driver!, shift.Start!.Value, shift.End!.Value)) ?? [],
    ];

    private static TimeOnly? Time(JsonObject json, string name) =>
        Text(json, name) is { } text && TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var time) ? time : null;

    /// <summary>The Open Exceptions of the TMS's list, or null when the answer isn't a list the Planner can read.</summary>
    public static IReadOnlyList<OpenExceptionSummary>? OpenExceptions(string body)
    {
        if (Parse(body) is not JsonArray exceptions)
        {
            return null;
        }

        var result = new List<OpenExceptionSummary>(exceptions.Count);
        foreach (var exception in exceptions.OfType<JsonObject>())
        {
            if (Text(exception, "exceptionNumber") is not { } number || Text(exception, "shipmentNumber") is not { } shipment || Text(exception, "type") is not { } type)
            {
                return null;
            }

            result.Add(new OpenExceptionSummary(number, shipment, type));
        }

        return result;
    }

    private static PlannerAnswer Read(
        int statusCode, string body, string? correlationHeader, LogOutcome success, string? shipmentNumber, int[] successStatuses, bool readTripNumber = false, string? knownTripNumber = null)
    {
        var json = Parse(body) as JsonObject;
        var correlationId = Text(json, "correlationId") ?? correlationHeader;

        if (successStatuses.Contains(statusCode) && json is not null)
        {
            return new PlannerAnswer(
                success, correlationId, shipmentNumber ?? Text(json, "shipmentNumber"), null, CarrierBookingNumber: Text(json, "carrierBookingNumber"),
                TripNumber: readTripNumber ? Text(json, "number") ?? knownTripNumber : null);
        }

        // A refusal names its rule by a stable code. Never its message, which may quote what was booked.
        if (statusCode is >= 400 and < 500 && Text(json, "code") is { } code)
        {
            return new PlannerAnswer(LogOutcome.PlanningRefused, correlationId, shipmentNumber, $"{code} ({statusCode})", code);
        }

        return new PlannerAnswer(LogOutcome.PlanningNotReceived, correlationId, shipmentNumber, $"The TMS API answered {statusCode} without an answer to the call.");
    }

    private static decimal Number(JsonObject json, string name) => json[name] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0;

    private static string? Text(JsonObject? json, string name) =>
        json?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static JsonNode? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
