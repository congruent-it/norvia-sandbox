namespace Norvia.HealthCheck.Application.Estate;

/// <summary>
/// The facts about the estate's configuration that the Checks look at. Locally a snapshot holds them; in Azure the adapter over
/// Resource Graph and Azure Monitor produces the same shape. A Check reads only these facts, never a resource.
///
/// Every fact defaults to the healthy value, so a snapshot or an adapter names only what it knows. A fact the adapter cannot read is
/// <see langword="null"/>, not healthy: a Check gives no Finding for what it could not see, and the docs say which those are.
/// </summary>
/// <param name="Name">What the facts describe, such as <c>as-found</c>. Not a resource name.</param>
/// <param name="ServiceBusEntities">Every queue and subscription with a dead-letter queue that a Logic App reads.</param>
public sealed record EstateConfiguration(string Name, IReadOnlyList<ServiceBusEntity> ServiceBusEntities)
{
    /// <summary>Every Logic App of the estate, by the name of its workflow, with the alert and the tracked properties it has.</summary>
    public IReadOnlyList<LogicAppFlow> Flows { get; init; } = [];

    /// <summary>How many receivers the alerts' action group has. None means the alerts exist and go nowhere.</summary>
    public int? AlertReceivers { get; init; } = 1;

    public StorageFacts Storage { get; init; } = new();

    /// <summary>Where a Carrier endpoint secret sits inline in a Logic App instead of in Key Vault, by location, never by value. Null when the adapter cannot read the settings.</summary>
    public IReadOnlyList<string>? InlinedCarrierSecrets { get; init; } = [];

    public RetentionFacts? Retention { get; init; } = new();

    public BusinessSafetyNetFacts BusinessSafetyNets { get; init; } = new();

    public ApiManagementFacts ApiManagement { get; init; } = new();

    /// <summary>Whether the Driver reports Logic App tells the Driver app what became of each report. Null when the adapter cannot read the workflow.</summary>
    public bool? DriverReportsReportBack { get; init; } = true;

    /// <summary>Whether the nightly conversation sweeper is running.</summary>
    public bool? ConversationSweeperRuns { get; init; } = true;

    /// <summary>What guards the agents. Null when the estate has no agents or the adapter cannot read them.</summary>
    public AgentFacts? Agents { get; init; }
}

/// <summary>A queue or subscription, by the name Service Bus reports it under, and whether an alert watches its dead-letter queue.</summary>
public sealed record ServiceBusEntity(string Name, bool HasDeadLetterAlert);
