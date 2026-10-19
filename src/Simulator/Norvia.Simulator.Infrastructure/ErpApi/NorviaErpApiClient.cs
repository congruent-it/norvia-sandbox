using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Invoicing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.PartnerApi;

namespace Norvia.Simulator.Infrastructure.ErpApi;

/// <summary>
/// Invoices through the ERP API as the Simulator's own identity, as the ERP UI does for Finance. Invoice now and a Credit note each go in a span of their
/// own, named like the ERP UI's (<c>Invoice now</c>, <c>Issue Credit note</c>), which is the Simulator's hop on the Message trail; once the answer is in it is
/// logged with the tracking contract's keys the Simulator knows (coding guidelines §7).
/// </summary>
internal sealed partial class NorviaErpApiClient(
    HttpClient http,
    IErpTokens tokens,
    SimulatorActivities activities,
    ILogger<NorviaErpApiClient> logger)
    : IErpApi
{
    private const string FlowName = "invoicing";

    public async Task<InvoiceNowAnswer> InvoiceNowAsync(string shipperId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipperId);

        using var span = activities.Source.StartActivity(InvoicingActions.InvoiceNow, ActivityKind.Internal);
        InvoiceNowAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/shipper-invoices", UriKind.Relative)) { Content = JsonContent.Create(new { ShipperId = shipperId }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            answer = ErpAnswers.Invoiced((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Unreachable, too slow, or no token to call with: nothing reached the ERP, so nothing was invoiced.
            LogNotSent(exception, InvoicingActions.InvoiceNow);
            answer = ErpAnswers.InvoiceNotReceived("The call didn't reach the ERP API.");
        }

        using (logger.BeginScope(TrackingKeysOf(answer.Outcome, answer.CorrelationId, answer.InvoiceNumber, answer.Detail, shipperId)))
        {
            LogAnswered(InvoicingActions.InvoiceNow, answer.Outcome);
        }

        return answer;
    }

    public async Task<CreditNoteAnswer> IssueCreditNoteAsync(
        string invoiceNumber, string transportOrderNumber, decimal amountWithoutVat, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportOrderNumber);

        using var span = activities.Source.StartActivity(InvoicingActions.IssueCreditNote, ActivityKind.Internal);
        CreditNoteAnswer answer;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/shipper-invoices/{Uri.EscapeDataString(invoiceNumber)}/credit-notes", UriKind.Relative))
            {
                Content = JsonContent.Create(new { TransportOrderNumber = transportOrderNumber, AmountWithoutVat = amountWithoutVat, Reason = reason }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetTokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            answer = ErpAnswers.CreditNote((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogNotSent(exception, InvoicingActions.IssueCreditNote);
            answer = ErpAnswers.CreditNoteNotReceived("The call didn't reach the ERP API.");
        }

        using (logger.BeginScope(TrackingKeysOf(answer.Outcome, answer.CorrelationId, answer.CreditNoteNumber ?? invoiceNumber, answer.Detail, shipperId: null)))
        {
            LogAnswered(InvoicingActions.IssueCreditNote, answer.Outcome);
        }

        return answer;
    }

    /// <summary>
    /// The tracking contract's keys the Simulator knows (coding guidelines §7), so its trace joins the ERP's Message trail. A key that doesn't apply is left
    /// out, never sent empty.
    /// </summary>
    internal static Dictionary<string, object?> TrackingKeysOf(LogOutcome outcome, string? correlationId, string? documentNumber, string? detail, string? shipperId)
    {
        var keys = new Dictionary<string, object?> { ["flow"] = FlowName };
        switch (outcome)
        {
            case LogOutcome.InvoiceIssued or LogOutcome.CreditNoteIssued or LogOutcome.NothingToInvoice:
                keys["outcome"] = "Succeeded";
                break;
            case LogOutcome.InvoicingRefused:
                keys["outcome"] = "Refused";
                keys["failureCode"] = detail?.Split(' ')[0];
                break;
            default:
                keys["outcome"] = "Failed";
                keys["failureCode"] = "InvoicingNotReceived";
                break;
        }

        foreach (var (key, value) in new[] { ("correlationId", correlationId), ("documentNumber", documentNumber), ("partyId", shipperId) })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys[key] = value;
            }
        }

        return keys;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Action} didn't reach the ERP API")]
    private partial void LogNotSent(Exception exception, string action);

    [LoggerMessage(Level = LogLevel.Information, Message = "The ERP answered {Action}: {Outcome}")]
    private partial void LogAnswered(string action, LogOutcome outcome);
}
