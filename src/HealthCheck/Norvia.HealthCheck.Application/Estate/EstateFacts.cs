namespace Norvia.HealthCheck.Application.Estate;

/// <summary>
/// A Logic App, by the name of its workflow (<c>carrier-tracking</c>), and what it should have. A null fact could not be read.
/// <paramref name="FailedRuns"/> is how many of its runs failed in the last <see cref="EstateConfiguration.FailureWindowDays"/> days.
/// </summary>
public sealed record LogicAppFlow(string Name, bool HasFailedRunAlert, bool? RecordsTrackedProperties = true, int? FailedRuns = 0);

/// <summary>Whether the storage protections exist. The healthy value is that every one does; null is a protection the adapter could not read.</summary>
/// <param name="InboundHasLifecycleRule">The inbound Proof of delivery container deletes a processed blob after a while.</param>
/// <param name="TransitHasLifecycleRule">The Driver app's transit container does too.</param>
/// <param name="ProofFilesMoveToCool">The TMS's Proof of delivery files leave the hot tier.</param>
/// <param name="InboundMalwareScanning">Defender for Storage scans what reaches the inbound container.</param>
/// <param name="InboundAccount">The inbound storage account's name, where the adapter reads a real estate. A snapshot has none.</param>
/// <param name="TransitAccount">The Driver app's transit storage account's name, likewise.</param>
public sealed record StorageFacts(
    bool? InboundHasLifecycleRule = true,
    bool? TransitHasLifecycleRule = true,
    bool? ProofFilesMoveToCool = true,
    bool? InboundMalwareScanning = true,
    string? InboundAccount = null,
    string? TransitAccount = null);

/// <summary>How long what the estate traces is kept, in days. The index that finds a run should keep what the run history keeps.</summary>
public sealed record RetentionFacts(int AppInsightsDays = 90, int RunHistoryDays = 90);

/// <summary>The business-side safety nets of the TMS. Each is set in the healthy estate; null is one the adapter could not read.</summary>
public sealed record BusinessSafetyNetFacts(bool? StaleThresholdsSet = true, bool? ProofMissingThresholdSet = true, bool? LateDeliveryCheckOn = true);

/// <summary>What API Management does to the Shipper tracking API: how long it caches an answer (0 for never) and whether it limits the rate.</summary>
public sealed record ApiManagementFacts(int? ShipperTrackingCacheSeconds = 0, bool? ShipperRateLimit = true);

/// <summary>What guards the estate's agents (ADR 0040), and how each agent's identity and budget are set.</summary>
/// <param name="ContentFilterAndPromptShield">The model deployment has a content filter and a prompt shield.</param>
/// <param name="Identities">One per agent.</param>
/// <param name="EnabledInPublicDemoUi"><c>Agents:Enabled</c> is on in a Demo UI the public can reach.</param>
/// <param name="ModelQuotaUnlimited">The model deployment has no quota to stop a runaway bill.</param>
public sealed record AgentFacts(
    bool ContentFilterAndPromptShield = true,
    IReadOnlyList<AgentIdentity>? Identities = null,
    bool EnabledInPublicDemoUi = false,
    bool ModelQuotaUnlimited = false)
{
    public IReadOnlyList<AgentIdentity> Identities { get; init; } = Identities ?? [];
}

/// <summary>One agent: the role its identity holds, and whether it has a cap on tool calls, a request-rate limit and a cost tag.</summary>
/// <param name="Role">The widest role the identity holds, such as <c>HealthCheck.Agent.Read</c>.</param>
/// <param name="RoleIsRead">The role only reads. Anything wider is a Finding.</param>
public sealed record AgentIdentity(string Name, string Role, bool RoleIsRead = true, bool HasCallCap = true, bool HasRateLimit = true, bool HasCostTag = true);
