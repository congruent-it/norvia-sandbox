namespace Norvia.Erp.Web.Agents;

/// <summary>The Explain button's logs. Here rather than in the component: the logging source generator can't see Razor's generated code.</summary>
internal static partial class AgentPageLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining line {CarrierBookingReference} of Carrier invoice {InvoiceNumber} failed with {FailureCode}")]
    public static partial void ExplainFailed(ILogger logger, string carrierBookingReference, string invoiceNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining line {CarrierBookingReference} of Carrier invoice {InvoiceNumber} failed")]
    public static partial void ExplainCrashed(ILogger logger, Exception exception, string carrierBookingReference, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Explaining Carrier invoice {InvoiceNumber} failed with {FailureCode}")]
    public static partial void ExplainInvoiceFailed(ILogger logger, string invoiceNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Explaining Carrier invoice {InvoiceNumber} failed")]
    public static partial void ExplainInvoiceCrashed(ILogger logger, Exception exception, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Drafting the rejection reason of line {CarrierBookingReference} of Carrier invoice {InvoiceNumber} failed with {FailureCode}")]
    public static partial void DraftReasonFailed(ILogger logger, string carrierBookingReference, string invoiceNumber, string failureCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Drafting the rejection reason of line {CarrierBookingReference} of Carrier invoice {InvoiceNumber} failed")]
    public static partial void DraftReasonCrashed(ILogger logger, Exception exception, string carrierBookingReference, string invoiceNumber);
}
