namespace Norvia.AppHost;

/// <summary>
/// Locally there is no Entra, so the TMS trusts tokens signed with a key the AppHost holds as a secret parameter.
/// The Aspire test host sets that key and issues its own tokens, with or without the intake role. The local intake
/// Logic App's client ID stands in for its managed identity, which in Azure is the only caller on the Shipper API channel.
/// The local booking worker's client ID stands in for the P4 booking Logic App's, the one holder of the report role.
/// The local Shipper notification worker's client ID stands in for the P7 Shipper notification Logic App's, the one holder of the Shipper notification report role.
/// The local Carrier cancellation worker's client ID stands in for the P7 Carrier cancellation Logic App's, which holds the Carrier booking report role beside the booking worker.
/// The local Trip dispatch worker's client ID stands in for the P11 Trip dispatch Logic App's, the one holder of the Trip delivery report role.
/// The local Driver reports worker's client ID stands in for the P12 Driver reports Logic App's, the one holder of the Trip start role and the Driver app's outcome role.
/// The local tracking worker's client ID stands in for the Carrier-tracking Logic App's, the one holder of the record role.
/// The local Proof of delivery worker's client ID stands in for the Carrier Proof of delivery Logic App's, the one holder of the Proof of delivery record role.
/// The local answer Logic App's client ID, which the stand-in Partner API signs its token with, is the one holder of the answer role.
/// The local API Management client ID, which the stand-in Partner API signs its Shipper tracking token with, is the one holder of the Shipper tracking role.
/// The local Billing Logic App's client ID stands in for the Billing Logic App's, the one holder of the ERP billing API's record role; the ERP API trusts the same key.
/// The local Shipper invoice Logic App's client ID stands in for the Shipper invoice Logic App's, the one holder of the ERP API's Shipper invoice report role.
/// The local Carrier invoice outcome Logic App's client ID stands in for the Carrier invoice outcome Logic App's, the one holder of the ERP API's Carrier invoice report role.
/// The local Carrier invoice Logic App's client ID stands in for the Carrier invoice Logic App's, the one holder of the ERP API's Carrier invoice receive role. Its trigger takes only API Management's client, as the workflow's first step does in Azure.
/// The Partner portal's panel signs its own token for the Driver app and TMS APIs, as its managed identity holding the Demo.Fault role, which guards every fault switch.
/// The TMS UI signs its own token for the TMS API with the key too, as its managed identity holding the manage role.
/// The Simulator signs its own token for the ERP API with the key too, as its managed identity holding the ERP API's manage role (Invoice now and a Credit note).
/// The local Partner API trusts the same key for the Personas' tokens, which the Partner portal signs itself (ADR 0023).
/// The Estate health check signs the Estate health-check agent's token itself, as its agent identity holding the call role, for the same stand-in, which then signs its own token for the app's agent API as that identity.
/// The TMS UI signs the Customer service agent's token itself, as its agent identity holding the one read role of the TMS API's agent view, for the same stand-in, which then signs its own token for the TMS API as that identity.
/// The ERP UI signs the Carrier invoice matching agent's token itself, as its agent identity holding the one read role of the ERP API's Finance agent view, for the same stand-in, which then signs its own token for the ERP API as that identity.
/// The Integration console signs the Integration operations agent's token itself, as its agent identity holding the agent read role, for the local MCP stand-in; the stand-in then signs its own token for the console's agent API as that identity (ADR 0041).
/// </summary>
public static class LocalTokens
{
    public const string SigningKeyParameter = "local-token-signing-key";
    public const string Issuer = "https://norvia.local/tokens";
    public const string TmsAudience = "api://norvia-tms";
    public const string ErpAudience = "api://norvia-erp";
    public const string BillingLogicAppClientId = "norvia-local-billing-logic-app";
    public const string MasterDataSyncLogicAppClientId = "norvia-local-master-data-sync-logic-app";
    public const string CarrierCostLogicAppClientId = "norvia-local-carrier-cost-logic-app";
    public const string IntakeLogicAppClientId = "norvia-local-intake-logic-app";
    public const string BookingLogicAppClientId = "norvia-local-booking-logic-app";
    public const string ShipperInvoiceLogicAppClientId = "norvia-local-shipper-invoice-logic-app";
    public const string CarrierInvoiceLogicAppClientId = "norvia-local-carrier-invoice-logic-app";
    public const string CarrierInvoiceTriggerAudience = "api://norvia-carrier-invoice-logic-app";
    public const string CarrierInvoiceOutcomeLogicAppClientId = "norvia-local-carrier-invoice-outcome-logic-app";
    public const string ShipperNotificationLogicAppClientId = "norvia-local-shipper-notification-logic-app";
    public const string CarrierCancellationLogicAppClientId = "norvia-local-carrier-cancellation-logic-app";
    public const string TripDispatchLogicAppClientId = "norvia-local-trip-dispatch-logic-app";
    public const string DriverReportsLogicAppClientId = "norvia-local-driver-reports-logic-app";
    public const string DriverAppAudience = "api://norvia-driver-app";
    public const string AnswerLogicAppClientId = "norvia-local-answer-logic-app";
    public const string TrackingLogicAppClientId = "norvia-local-tracking-logic-app";
    public const string ProofOfDeliveryLogicAppClientId = "norvia-local-proof-of-delivery-logic-app";
    public const string ApiManagementClientId = "norvia-local-api-management";
    public const string TmsUiClientId = "norvia-local-tms-ui";
    public const string ErpUiClientId = "norvia-local-erp-ui";
    public const string SimulatorClientId = "norvia-local-simulator";
    public const string PartnerPortalClientId = "norvia-local-partner-portal";
    public const string PartnerApiAudience = "api://norvia-partner-api";

    /// <summary>The agent view's audience, which an Agent's token is for (the MCP stand-in checks it, as API Management does).</summary>
    public const string AgentAudience = "api://norvia-integration-console-agent";

    /// <summary>The console's agent API's audience, which the MCP server's token for the backend is for.</summary>
    public const string ConsoleAgentApiAudience = "api://norvia-integration-console-agent-api";

    /// <summary>The Integration operations agent's local identity, which stands in for its Entra agent identity.</summary>
    public const string IntegrationOperationsAgentClientId = "norvia-local-agent-integration-operations";

    /// <summary>The app role an Agent holds on the Integration console's agent API.</summary>
    public const string AgentReadRole = "IntegrationConsole.Agent.Read";

    /// <summary>The Estate health check's agent view audience, which the Estate health-check agent's token is for (the MCP stand-in checks it, as API Management does).</summary>
    public const string HealthCheckAgentAudience = "api://norvia-health-check-agent";

    /// <summary>The Estate health check's agent API audience, which the MCP server's token for the backend is for.</summary>
    public const string HealthCheckAgentApiAudience = "api://norvia-health-check-agent-api";

    /// <summary>The Estate health-check agent's local identity, which stands in for its Entra agent identity. It holds only the call role: no Azure role.</summary>
    public const string EstateHealthCheckAgentClientId = "norvia-local-agent-estate-health-check";

    /// <summary>The FinOps agent's view audience (#511), which its token is for. Its own, so the health-check agent's token opens nothing on its server.</summary>
    public const string FinOpsAgentAudience = "api://norvia-health-check-cost-agent";

    /// <summary>The FinOps agent's local identity, which stands in for its Entra agent identity. It holds only the cost call role: no Azure role.</summary>
    public const string FinOpsAgentClientId = "norvia-local-agent-finops";

    /// <summary>The app role the FinOps agent holds on the health-check API: the two cost tools.</summary>
    public const string FinOpsAgentReadRole = "HealthCheck.Cost.Read";

    /// <summary>The Customer service agent's view audience, which the Customer service agent's token is for (the MCP stand-in checks it, as API Management does).</summary>
    public const string CustomerServiceAgentAudience = "api://norvia-customer-service-agent";

    /// <summary>The Customer service agent's local identity, which stands in for its Entra agent identity. It holds only the one read role on the TMS API.</summary>
    public const string CustomerServiceAgentClientId = "norvia-local-agent-customer-service";

    /// <summary>The app role the Customer service agent holds on the TMS API's agent view.</summary>
    public const string CustomerServiceAgentReadRole = "TransportOrders.CustomerServiceAgent.Read";

    /// <summary>The Customer service reply agent's view audience: its own, so the Customer service agent's token is not for it.</summary>
    public const string CustomerServiceReplyAgentAudience = "api://norvia-customer-service-reply-agent";

    /// <summary>The Customer service reply agent's local identity, which stands in for its Entra agent identity. It holds only its own read role.</summary>
    public const string CustomerServiceReplyAgentClientId = "norvia-local-agent-customer-service-reply";

    /// <summary>The app role the Customer service reply agent holds on the TMS API's reply agent view.</summary>
    public const string CustomerServiceReplyAgentReadRole = "TransportOrders.CustomerServiceReplyAgent.Read";

    /// <summary>The Carrier invoice matching agent's view audience, which its token is for (the MCP stand-in checks it, as API Management does). Its own, so no other Agent's token is for it.</summary>
    public const string FinanceAgentAudience = "api://norvia-finance-matching-agent";

    /// <summary>The Carrier invoice matching agent's local identity, which stands in for its Entra agent identity. It holds only the one read role on the ERP API.</summary>
    public const string FinanceAgentClientId = "norvia-local-agent-finance-matching";

    /// <summary>The app role the Carrier invoice matching agent holds on the ERP API's Finance agent view.</summary>
    public const string FinanceAgentReadRole = "CarrierInvoices.FinanceAgent.Read";

    /// <summary>The Carrier invoice rejection reason agent's view audience: its own, so the Carrier invoice matching agent's token is not for it.</summary>
    public const string FinanceRejectionReasonAgentAudience = "api://norvia-finance-rejection-reason-agent";

    /// <summary>The Carrier invoice rejection reason agent's local identity, which stands in for its Entra agent identity. It holds only its own read role.</summary>
    public const string FinanceRejectionReasonAgentClientId = "norvia-local-agent-finance-rejection-reason";

    /// <summary>The app role the Carrier invoice rejection reason agent holds on the ERP API's rejection reason agent view.</summary>
    public const string FinanceRejectionReasonAgentReadRole = "CarrierInvoices.FinanceRejectionReasonAgent.Read";

    /// <summary>The Planner agent's view audience: its own, so no other Agent's token is for it.</summary>
    public const string PlannerAgentAudience = "api://norvia-planner-agent";

    /// <summary>The Planner agent's local identity, which stands in for its Entra agent identity. It holds only its own read role.</summary>
    public const string PlannerAgentClientId = "norvia-local-agent-planner";

    /// <summary>The app role the Planner agent holds on the TMS API's planner agent view.</summary>
    public const string PlannerAgentReadRole = "TransportOrders.PlannerAgent.Read";

    /// <summary>The Planner notification agent's view audience: its own, so no other Agent's token is for it.</summary>
    public const string PlannerNotificationAgentAudience = "api://norvia-planner-notification-agent";

    /// <summary>The Planner notification agent's local identity, which stands in for its Entra agent identity. It holds only its own read role.</summary>
    public const string PlannerNotificationAgentClientId = "norvia-local-agent-planner-notification";

    /// <summary>The app role the Planner notification agent holds on the TMS API's planner notification agent view.</summary>
    public const string PlannerNotificationAgentReadRole = "TransportOrders.PlannerNotificationAgent.Read";

    /// <summary>The app role the Estate health-check agent holds on the health-check API.</summary>
    public const string HealthCheckAgentReadRole = "HealthCheck.Agent.Read";

    /// <summary>The app role a Shipper Persona holds on the Norvia Partner API, as the estate grants it in Entra.</summary>
    public const string SubmitRole = "TransportOrders.Submit";

    /// <summary>The app role a Shipper Persona holds on the Norvia Partner API to ask the Intake agent for a Booking draft.</summary>
    public const string DraftRole = "TransportOrders.Draft";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API, as the estate grants it in Entra.</summary>
    public const string RespondRole = "CarrierBookings.Respond";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API to report tracking, as the estate grants it in Entra.</summary>
    public const string TrackingSubmitRole = "Tracking.Submit";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API to submit Proofs of delivery, as the estate grants it in Entra.</summary>
    public const string ProofsOfDeliverySubmitRole = "ProofsOfDelivery.Submit";

    /// <summary>The app role a Carrier Persona holds on the Norvia Partner API to submit Carrier invoices, as the estate grants it in Entra. A Shipper Persona doesn't.</summary>
    public const string CarrierInvoicesSubmitRole = "CarrierInvoices.Submit";

    /// <summary>The app role a Shipper Persona holds on the Norvia Partner API to read its tracking, as the estate grants it in Entra. A Carrier Persona doesn't.</summary>
    public const string TrackingReadRole = "Tracking.Read";

    /// <summary>A Persona's local client ID, which stands in for its Entra client app.</summary>
    public static string PersonaClientId(string personaId) => $"norvia-local-persona-{personaId}";
}
