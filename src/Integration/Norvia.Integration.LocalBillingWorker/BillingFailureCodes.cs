namespace Norvia.Integration.LocalBillingWorker;

/// <summary>
/// The failure codes of the Billing flow, under the names the workflow's tracked properties and its dead-letter reason use
/// (coding guidelines §7). A refusal keeps the ERP's own code.
/// </summary>
internal static class BillingFailureCodes
{
    public const string Unreadable = "BillableLineUnreadable";
    public const string ErpUnavailable = "ErpUnavailable";
    public const string ErpCallRefused = "ErpCallRefused";
}
