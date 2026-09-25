using System.Text.Json.Nodes;

namespace Norvia.Integration.TransportOrderIntake;

/// <summary>Norvia's answer to a partner booking: the HTTP status and the body, in the Norvia Partner API's format.</summary>
/// <param name="FailureCode">
/// Why the booking was not received, for the run and the tracking contract's <c>failureCode</c>; null when the TMS
/// took the booking in.
/// </param>
public sealed record BookingResponse(int StatusCode, JsonObject Body, string? FailureCode = null)
{
    /// <summary>The answer as the intake Logic App reads it: <c>statusCode</c>, <c>body</c> and, when there is one, <c>failureCode</c>.</summary>
    public string ToJsonString()
    {
        var answer = new JsonObject { ["statusCode"] = StatusCode, ["body"] = Body.DeepClone() };
        if (FailureCode is not null)
        {
            answer["failureCode"] = FailureCode;
        }

        return answer.ToJsonString();
    }
}
