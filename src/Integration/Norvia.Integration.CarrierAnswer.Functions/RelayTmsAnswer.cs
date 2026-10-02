using Microsoft.Azure.Functions.Extensions.Workflows;
using Microsoft.Azure.Functions.Worker;
using Norvia.Integration.CarrierBooking;

namespace Norvia.Integration.CarrierAnswer.Functions;

/// <summary>Relays the TMS's response to the Carrier in the Carrier answer response format, as a local function of the Carrier-answer Logic App.</summary>
public sealed class RelayTmsAnswer
{
    /// <param name="mappedAnswer">What <see cref="MapCarrierAnswer"/> returned.</param>
    /// <param name="tmsStatusCode">The status the TMS answered with, or 0 when it didn't answer or wasn't called.</param>
    /// <param name="tmsResponse">The TMS's answer as JSON text, or empty when it didn't answer.</param>
    /// <param name="correlationId">The correlation ID of the answer.</param>
    /// <returns>The answer for the Carrier as JSON text: its <c>statusCode</c>, <c>body</c> and, when it wasn't recorded for a reason the run should show, <c>failureCode</c>.</returns>
    [Function(nameof(RelayTmsAnswer))]
    public Task<string> RunAsync([WorkflowActionTrigger] string mappedAnswer, int tmsStatusCode, string tmsResponse, string correlationId) =>
        Task.FromResult(CarrierAnswerResponses.Relay(MappedCarrierAnswer.FromJson(mappedAnswer), tmsStatusCode, tmsResponse, correlationId).ToJsonString());
}
