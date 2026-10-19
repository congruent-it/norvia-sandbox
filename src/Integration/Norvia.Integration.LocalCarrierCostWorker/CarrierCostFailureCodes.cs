namespace Norvia.Integration.LocalCarrierCostWorker;

/// <summary>
/// The failure codes of the CarrierCost flow, under the names the workflow's tracked properties and its dead-letter reason use
/// (coding guidelines §7). A refusal keeps the ERP's own code.
/// </summary>
internal static class CarrierCostFailureCodes
{
    public const string Unreadable = "CarrierCostUnreadable";
    public const string ErpUnavailable = "ErpUnavailable";
    public const string ErpCallRefused = "ErpCallRefused";
}
