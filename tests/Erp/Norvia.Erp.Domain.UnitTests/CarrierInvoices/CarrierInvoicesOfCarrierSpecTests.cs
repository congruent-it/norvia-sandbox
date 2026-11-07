using Norvia.Erp.Domain.CarrierInvoices;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Domain.UnitTests.CarrierInvoices;

public sealed class CarrierInvoicesOfCarrierSpecTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static CarrierInvoice Invoice(string carrierId, string number, DateTimeOffset at) =>
        CarrierInvoice.Receive(
            new CarrierInvoiceData(new CarrierId(carrierId), number, new DateOnly(2026, 10, 19), [new CarrierInvoiceLineData("TRX-1", 5m, null)], "invoice"), [], 0m, at).Invoice!;

    [Fact]
    public void OfCarrier_KeepsEveryInvoiceOfThatCarrier_TheOldestFirst_AndNoOthers()
    {
        var older = Invoice("CAR-0001", "A-1", Now);
        var newer = Invoice("CAR-0001", "A-2", Now.AddHours(1));

        new CarrierInvoicesOfCarrierSpec(new CarrierId("CAR-0001")).Evaluate([newer, Invoice("CAR-0002", "B-1", Now), older]).Should().Equal(older, newer);
    }
}
