using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;

namespace Norvia.Integration.TransportOrderIntake.Functions;

/// <summary>Relays the TMS's outcome in the partner format, as a local function of the intake Logic App.</summary>
public sealed class RelayTmsOutcome
{
    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    /// <param name="correlationId">The correlation ID of the attempt.</param>
    /// <param name="partnerBooking">The booking as the JSON text the Shipper sent.</param>
    /// <returns>The answer for the Shipper as JSON text: its <c>statusCode</c>, <c>body</c> and, when it wasn't received, <c>failureCode</c>.</returns>
    [Function(nameof(RelayTmsOutcome))]
    public Task<string> RunAsync([WorkflowActionTrigger] int tmsStatusCode, string tmsResponse, string correlationId, string partnerBooking) =>
        Task.FromResult(BookingResponses.Relay(tmsStatusCode, tmsResponse, correlationId, partnerBooking).ToJsonString());
}
