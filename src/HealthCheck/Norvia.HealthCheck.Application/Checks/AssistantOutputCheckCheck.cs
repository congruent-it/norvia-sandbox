using Norvia.HealthCheck.Application.Estate;

namespace Norvia.HealthCheck.Application.Checks;

/// <summary>The portal checks the Shipper tracking assistant's answer in code (ADR 0049).</summary>
public sealed class AssistantOutputCheckCheck : Check
{
    public const string CheckId = "ASSISTANT-OUTPUT-CHECK";

    public override string Id => CheckId;

    public override string Rule => "The Partner portal checks every answer of the Shipper tracking assistant against the tracking data before the Shipper sees it (ADR 0049).";

    public override string Title => "The tracking assistant's answer is not checked";

    public override Severity Severity => Severity.High;

    public override string AsFoundToggle => "assistantOutputCheckOff";

    public override string Fix =>
        "Set assistantOutputCheckOff to false and provision the estate: the portal then drops an answer whose field or value is not in the data.";

    public override IReadOnlyList<Evidence> Examine(EstateConfiguration estate)
    {
        ArgumentNullException.ThrowIfNull(estate);

        return estate.Agents?.TrackingAssistant is { OutputCheckOn: false }
            ? [new Evidence(TrackingAssistantFacts.AgentName, "A date, a number or an order the data does not hold can reach a Shipper as a promise Norvia did not make.")]
            : [];
    }
}
