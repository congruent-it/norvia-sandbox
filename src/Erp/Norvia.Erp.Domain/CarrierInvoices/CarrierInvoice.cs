using Ardalis.SharedKernel;
using Norvia.Erp.Domain.CarrierCosts;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.CarrierInvoices;

/// <summary>
/// What a Carrier asks to be paid for any number of its Carrier bookings. It is recorded and matched in one step: each line is matched
/// against the Carrier cost with its Carrier booking reference, a Matched line is accepted at once and its Carrier cost becomes Invoiced,
/// and an invoice whose lines all match is Approved straight away. Otherwise it is Received, and its other lines wait for Finance.
/// </summary>
public sealed class CarrierInvoice : EntityBase<CarrierInvoiceId>, IAggregateRoot
{
    public const int MaxInvoiceNumberLength = 50;

    /// <summary>The longest remark or reason Finance can give when it decides a line.</summary>
    public const int MaxRemarkLength = 500;

    /// <summary>The longest technical cause kept when the outcome is reported not delivered.</summary>
    public const int MaxCauseLength = 500;

    private CarrierInvoice()
    {
    }

    private CarrierInvoice(CarrierInvoiceData data, IReadOnlyList<CarrierInvoiceLine> lines, DateTimeOffset receivedAt)
    {
        Id = CarrierInvoiceId.New();
        CarrierId = data.CarrierId;
        InvoiceNumber = data.InvoiceNumber;
        InvoiceDate = data.InvoiceDate;
        Lines = lines;
        CorrelationId = data.CorrelationId;
        ReceivedAt = receivedAt;
        Total = lines.Sum(line => line.Amount);
        if (lines.All(line => line.Status == CarrierInvoiceLineStatus.Matched))
        {
            Status = CarrierInvoiceStatus.Approved;
            ApprovedAt = receivedAt;
            ApprovedAmount = Total;
        }
        else
        {
            Status = CarrierInvoiceStatus.Received;
        }
    }

    public CarrierId CarrierId { get; private set; }

    /// <summary>The Carrier's own invoice number, unique per Carrier.</summary>
    public string InvoiceNumber { get; private set; } = string.Empty;

    public DateOnly InvoiceDate { get; private set; }

    public IReadOnlyList<CarrierInvoiceLine> Lines { get; private set; } = [];

    /// <summary>What the Carrier claims in all, in EUR without VAT: the sum of the lines.</summary>
    public decimal Total { get; private set; }

    /// <summary>The correlation ID API Management created for the Carrier invoice; the event carries it.</summary>
    public string CorrelationId { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAt { get; private set; }

    public CarrierInvoiceStatus Status { get; private set; }

    /// <summary>The sum of the Matched and Accepted amounts; null until the invoice is Approved.</summary>
    public decimal? ApprovedAmount { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>When the Carrier invoice outcome Logic App reported the Carrier told the outcome; null until then.</summary>
    public DateTimeOffset? ToldAt { get; private set; }

    /// <summary>When the Carrier invoice outcome Logic App reported the outcome not delivered; null unless the Carrier is not told.</summary>
    public DateTimeOffset? NotToldAt { get; private set; }

    /// <summary>The Carrier endpoint's HTTP status code of an outcome not delivered; null when the failure had none, such as a timeout.</summary>
    public int? NotToldStatusCode { get; private set; }

    /// <summary>The technical cause of an outcome not delivered, in plain language.</summary>
    public string? NotToldMessage { get; private set; }

    /// <summary>
    /// Records that the Carrier's invoice outcome endpoint acknowledged the outcome. Only an Approved invoice whose outcome has not been
    /// reported yet can be reported; false for any other, which the caller ignores and logs.
    /// </summary>
    public bool ReportTold(DateTimeOffset at)
    {
        if (!OutcomeAwaitsReport)
        {
            return false;
        }

        ToldAt = at;
        return true;
    }

    /// <summary>
    /// Records that the outcome never reached the Carrier, with the technical cause: the invoice is flagged Carrier not told. Only an
    /// Approved invoice whose outcome has not been reported yet can be reported; false for any other, which the caller ignores and logs.
    /// </summary>
    public bool ReportNotDelivered(int? statusCode, string message, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!OutcomeAwaitsReport)
        {
            return false;
        }

        NotToldAt = at;
        NotToldStatusCode = statusCode;
        NotToldMessage = message.Length > MaxCauseLength ? message[..MaxCauseLength] : message;
        return true;
    }

    private bool OutcomeAwaitsReport => Status == CarrierInvoiceStatus.Approved && ToldAt is null && NotToldAt is null;

    /// <summary>
    /// Receives a Carrier invoice: checks the receiving rules, matches each line against <paramref name="costs"/>, and makes each Matched
    /// line's Carrier cost Invoiced. <paramref name="costs"/> are the Carrier's own Carrier costs with the lines' references, so a
    /// reference of another Carrier is an Unknown booking. Whether the Carrier exists, and whether the invoice number was used, are for
    /// the caller to check.
    /// </summary>
    /// <param name="tolerance">How far, in EUR, a line's amount may be from the Carrier cost's total and still match.</param>
    public static (CarrierInvoice? Invoice, CarrierInvoiceRefusal? Refusal) Receive(
        CarrierInvoiceData data, IReadOnlyList<CarrierCost> costs, decimal tolerance, DateTimeOffset receivedAt)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (Refuse(data) is { } refusal)
        {
            return (null, refusal);
        }

        var lines = data.Lines.Select(line => Match(line, costs, tolerance)).ToList();
        return (new CarrierInvoice(data, lines, receivedAt), null);
    }

    /// <summary>
    /// A Carrier cost has arrived: matches again every undecided Unknown booking line of this Carrier invoice that quotes its reference.
    /// The line gets its new result (Matched, Price mismatch or Duplicate) and a Matched line is accepted at once, making the Carrier cost
    /// Invoiced. A Carrier cost of another Carrier, and lines Finance already decided, are left alone. Returns whether this made the invoice
    /// Approved.
    /// </summary>
    /// <param name="tolerance">How far, in EUR, a line's amount may be from the Carrier cost's total and still match.</param>
    public bool MatchAgain(CarrierCost cost, decimal tolerance, DateTimeOffset matchedAt)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (cost.CarrierId != CarrierId || Status != CarrierInvoiceStatus.Received)
        {
            return false;
        }

        var changed = false;
        var lines = Lines.Select(line =>
        {
            if (line.Status != CarrierInvoiceLineStatus.ToDecide
                || line.Result != CarrierInvoiceLineResult.UnknownBooking
                || line.CarrierBookingReference != cost.CarrierBookingReference)
            {
                return line;
            }

            changed = true;
            return Match(new CarrierInvoiceLineData(line.CarrierBookingReference, line.Amount, line.Description), [cost], tolerance);
        }).ToList();

        if (!changed)
        {
            return false;
        }

        // A new list, so the change is seen as one.
        Lines = lines;
        return ApproveWhenDecided(matchedAt);
    }

    /// <summary>
    /// Finance accepts a line To decide, at the invoiced amount or, with a remark, at an adjusted one, and its Carrier cost becomes
    /// Invoiced. <paramref name="cost"/> is the Carrier cost the line was matched against; a line without an Open Carrier cost for this
    /// Carrier can only be rejected. Returns whether this decision made the invoice Approved.
    /// </summary>
    public (bool Approved, CarrierInvoiceRefusal? Refusal) Accept(
        string carrierBookingReference, decimal? adjustedAmount, string? remark, CarrierCost? cost, DateTimeOffset decidedAt)
    {
        var index = IndexOfLineToDecide(carrierBookingReference, out var refusal);
        if (refusal is not null)
        {
            return (false, refusal);
        }

        var line = Lines[index];
        if (cost is null || line.CarrierBookingNumber != cost.Id.CarrierBookingNumber || cost.CarrierId != CarrierId || cost.Status != CarrierCostStatus.Open)
        {
            return (false, CarrierInvoiceRefusal.LineHasNoOpenCarrierCost(InvoiceNumber, carrierBookingReference));
        }

        var amount = adjustedAmount ?? line.Amount;
        if (amount <= 0)
        {
            return (false, CarrierInvoiceRefusal.AdjustedAmountNotAboveZero(InvoiceNumber, carrierBookingReference, amount));
        }

        var adjusted = amount != line.Amount;
        if (adjusted && string.IsNullOrWhiteSpace(remark))
        {
            return (false, CarrierInvoiceRefusal.AdjustedAmountNeedsRemark(InvoiceNumber, carrierBookingReference));
        }

        cost.MarkInvoiced();
        return (Decide(index, line with { Status = CarrierInvoiceLineStatus.Accepted, DecidedAmount = amount, Remark = adjusted ? remark!.Trim() : null }, decidedAt), null);
    }

    /// <summary>
    /// Finance rejects a line To decide, with a reason. Its Carrier cost, if any, stays as it was. Returns whether this decision made
    /// the invoice Approved.
    /// </summary>
    public (bool Approved, CarrierInvoiceRefusal? Refusal) Reject(string carrierBookingReference, string? reason, DateTimeOffset decidedAt)
    {
        var index = IndexOfLineToDecide(carrierBookingReference, out var refusal);
        if (refusal is not null)
        {
            return (false, refusal);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, CarrierInvoiceRefusal.RejectionNeedsReason(InvoiceNumber, carrierBookingReference));
        }

        return (Decide(index, Lines[index] with { Status = CarrierInvoiceLineStatus.Rejected, Remark = reason.Trim() }, decidedAt), null);
    }

    private int IndexOfLineToDecide(string carrierBookingReference, out CarrierInvoiceRefusal? refusal)
    {
        var index = Lines.ToList().FindIndex(line => line.CarrierBookingReference == carrierBookingReference);
        refusal = index < 0
            ? CarrierInvoiceRefusal.LineNotFound(InvoiceNumber, carrierBookingReference)
            : Lines[index].Status != CarrierInvoiceLineStatus.ToDecide
                ? CarrierInvoiceRefusal.LineAlreadyDecided(InvoiceNumber, carrierBookingReference)
                : null;
        return index;
    }

    private bool Decide(int index, CarrierInvoiceLine decided, DateTimeOffset decidedAt)
    {
        // A new list, so the change is seen as one.
        Lines = [.. Lines.Select((line, at) => at == index ? decided : line)];
        return ApproveWhenDecided(decidedAt);
    }

    private bool ApproveWhenDecided(DateTimeOffset decidedAt)
    {
        if (Lines.Any(line => line.Status == CarrierInvoiceLineStatus.ToDecide))
        {
            return false;
        }

        Status = CarrierInvoiceStatus.Approved;
        ApprovedAt = decidedAt;
        ApprovedAmount = Lines.Sum(line => line.AcceptedAmount ?? 0m);
        return true;
    }

    /// <summary>
    /// Whether a repeat carries the same content as this Carrier invoice. The correlation ID of the message is left out: a Logic App's
    /// retry is the same Carrier invoice whichever run delivers it.
    /// </summary>
    public bool HasSameContentAs(CarrierInvoiceData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CarrierId == data.CarrierId
            && InvoiceNumber == data.InvoiceNumber
            && InvoiceDate == data.InvoiceDate
            && Lines.Count == data.Lines.Count
            && Lines.OrderBy(line => line.CarrierBookingReference, StringComparer.Ordinal)
                .Select(line => (line.CarrierBookingReference, line.Amount, line.Description))
                .SequenceEqual(
                    data.Lines.OrderBy(line => line.CarrierBookingReference, StringComparer.Ordinal)
                        .Select(line => (line.CarrierBookingReference, line.Amount, line.Description)));
    }

    private static CarrierInvoiceRefusal? Refuse(CarrierInvoiceData data)
    {
        if (data.Lines.Count == 0)
        {
            return CarrierInvoiceRefusal.NoLines(data.InvoiceNumber);
        }

        for (var index = 0; index < data.Lines.Count; index++)
        {
            var line = data.Lines[index];
            if (string.IsNullOrWhiteSpace(line.CarrierBookingReference))
            {
                return CarrierInvoiceRefusal.LineWithoutReference(data.InvoiceNumber, index + 1);
            }

            if (line.Amount <= 0)
            {
                return CarrierInvoiceRefusal.LineAmountNotAboveZero(data.InvoiceNumber, index + 1, line.Amount);
            }
        }

        var repeated = data.Lines.GroupBy(line => line.CarrierBookingReference, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        return repeated is null ? null : CarrierInvoiceRefusal.ReferenceRepeated(data.InvoiceNumber, repeated.Key);
    }

    private static CarrierInvoiceLine Match(CarrierInvoiceLineData line, IReadOnlyList<CarrierCost> costs, decimal tolerance)
    {
        // An Open Carrier cost first, so a reference that somehow has two is matched before it is called a Duplicate.
        var cost = costs
            .Where(candidate => candidate.CarrierBookingReference == line.CarrierBookingReference)
            .OrderBy(candidate => candidate.Status)
            .FirstOrDefault();

        if (cost is null)
        {
            return Line(line, CarrierInvoiceLineResult.UnknownBooking, null);
        }

        if (cost.Status == CarrierCostStatus.Invoiced)
        {
            return Line(line, CarrierInvoiceLineResult.Duplicate, cost);
        }

        if (Math.Abs(line.Amount - cost.Total) > tolerance)
        {
            return Line(line, CarrierInvoiceLineResult.PriceMismatch, cost);
        }

        cost.MarkInvoiced();
        return Line(line, CarrierInvoiceLineResult.Matched, cost);
    }

    private static CarrierInvoiceLine Line(CarrierInvoiceLineData line, CarrierInvoiceLineResult result, CarrierCost? cost) =>
        new(
            line.CarrierBookingReference,
            line.Amount,
            line.Description,
            result,
            result == CarrierInvoiceLineResult.Matched ? CarrierInvoiceLineStatus.Matched : CarrierInvoiceLineStatus.ToDecide,
            cost?.Id.CarrierBookingNumber);
}
