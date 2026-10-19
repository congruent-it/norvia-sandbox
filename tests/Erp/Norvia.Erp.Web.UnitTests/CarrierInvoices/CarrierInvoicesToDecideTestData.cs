using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.CarrierCosts;
using Norvia.Erp.Application.CarrierInvoices;

namespace Norvia.Erp.Web.UnitTests.CarrierInvoices;

internal static class CarrierInvoicesToDecideTestData
{
    public static CarrierCostView Cost(string status = "Open", bool spot = true) => new(
        "CB-0000001", "TRX-1", "CAR-0001", "SH-0000001", "TO-0000001", "EUR", 70m, [new ChargeLineView("Freight", 70m)],
        spot ? new CarrierCostSourceView("Spot", null, null, "Urgent load") : new CarrierCostSourceView("CarrierRateCard", "CRC-0001", 1, null),
        DateTimeOffset.UnixEpoch, "delivered", DateTimeOffset.UnixEpoch, status);

    public static CarrierInvoiceLineView Line(string reference, string result, string status = "ToDecide", decimal amount = 80m, string? remark = null) =>
        new(reference, amount, null, result, status, result == "UnknownBooking" ? null : "CB-0000001", status == "Accepted" ? amount : null, remark);

    public static CarrierInvoiceToDecide Item(string number, CarrierCostView? cost, params CarrierInvoiceLineView[] lines) => new(
        "Van Dijk Transport",
        new CarrierInvoiceView(
            "CAR-0001", number, new DateOnly(2026, 10, 19), "EUR", lines.Sum(line => line.Amount), lines, "invoice-1", DateTimeOffset.UnixEpoch, "Received", null, null),
        cost is null ? new Dictionary<string, CarrierCostView>() : new Dictionary<string, CarrierCostView> { [cost.CarrierBookingNumber] = cost });
}
