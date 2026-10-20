using System.Net;
using System.Net.Http.Json;

namespace Norvia.Erp.Api.IntegrationTests.Reset;

/// <summary>
/// The Simulator's reset at the ERP API (ADR 0039): with the setting on and the Simulator's role, and the subscriptions that record into the ERP and its
/// own outbox quiet, the ERP deletes its transactional records and keeps its master data.
/// </summary>
public sealed class ErpResetTests(ErpFixture fixture) : ResetTest(fixture)
{
    [Fact]
    public async Task AResetWithTheResetRole_DeletesEveryKindOfTransactionalRecord_AndAnswersWhatItDeletedByKind()
    {
        await RecordOneOfEveryKindAsync();
        (await HeldAsync()).Should().Be((1, 1, 1, 1, 1));

        var deleted = await ResetSucceedsAsync();

        foreach (var kind in new[] { "Credit notes", "Shipper invoices", "Billable lines", "Carrier costs", "Carrier invoices", "Invoice number sequences", "Credit note number sequences", "Messages" })
        {
            deleted.Should().ContainKey(kind);
            deleted[kind].Should().BePositive($"the reset deleted {kind}");
        }

        deleted["Shipper invoices"].Should().Be(1);
        deleted["Credit notes"].Should().Be(1);
        deleted["Billable lines"].Should().Be(3);
        deleted["Carrier costs"].Should().Be(3);
        deleted["Carrier invoices"].Should().Be(1, "its line decisions go with it");
        (await HeldAsync()).Should().Be((0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task AResetOnAnEmptyErp_Succeeds_WithNothingDeleted()
    {
        await ResetSucceedsAsync();

        var deleted = await ResetSucceedsAsync();

        deleted.Values.Should().OnlyContain(count => count == 0);
    }

    [Fact]
    public async Task AfterAReset_TheMasterDataStays_TheNumbersStartAgain_AndTheSameCarrierInvoiceNumberIsNotADuplicate()
    {
        var invoiceNumber = await RecordOneOfEveryKindAsync();
        await ResetSucceedsAsync();

        // Shippers, Carriers and their Rate Cards are master data: a Billable line for a seeded Shipper is still taken, and invoiced under the first number again.
        await RecordAsync(Billable("TO-0000401", freight: 150m));
        (await InvoiceAsync(Brouwer)).InvoiceNumber.Should().Be(invoiceNumber);
        await RecordCostAsync("CB-0000101", "BK-A", 100m);
        using var again = await PostInvoiceAsync(CarrierInvoice(Carrier, CarrierInvoiceNumber, ("BK-A", 100m)));

        again.StatusCode.Should().Be(HttpStatusCode.Created, "a run after a reset books the same numbers, which the ERP no longer remembers");
    }

    [Fact]
    public async Task TheReadiness_SaysTheResetIsSwitchedOn_AndNamesNoSubscriptionWhenAllAreQuiet()
    {
        using var client = Fixture.ApiAs(ErpFixture.ResetRole);

        var readiness = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>(new Uri("/v1/reset", UriKind.Relative), Ct);

        readiness!["enabled"]!.GetValue<bool>().Should().BeTrue();
        readiness["busySubscription"].Should().BeNull();
    }

    [Fact]
    public async Task AResetWithoutTheResetRole_IsRefusedWith403_AndDeletesNothing()
    {
        await RecordOneOfEveryKindAsync();
        var held = await HeldAsync();

        // Every other identity of the billing API manages something and is still no caller for the reset.
        foreach (var role in new[] { ErpFixture.RecordRole, ErpFixture.ReportRole, ErpFixture.RecordCarrierCostRole, ErpFixture.ReceiveCarrierInvoiceRole, ErpFixture.ReportCarrierInvoiceRole, ErpFixture.ManageCarrierInvoiceRole })
        {
            using var response = await ResetAsync(role);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} isn't the Simulator's role");
        }

        using var noRole = await ResetAsync("Finance");
        using var anonymous = Fixture.ApiWith(token: null);
        using var anonymousResponse = await anonymous.PostAsync(new Uri("/v1/reset", UriKind.Relative), content: null, Ct);
        using var anonymousReadiness = await anonymous.GetAsync(new Uri("/v1/reset", UriKind.Relative), Ct);
        using var readinessWithoutRole = await Fixture.ApiAs(ErpFixture.ManageCarrierInvoiceRole).GetAsync(new Uri("/v1/reset", UriKind.Relative), Ct);

        noRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymousReadiness.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        readinessWithoutRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await HeldAsync()).Should().Be(held);
    }

    [Theory]
    [InlineData("transport-order-events/billing")]
    [InlineData("shipment-events/carrier-cost")]
    [InlineData("invoicing-events/shipper-invoice")]
    [InlineData("invoicing-events/carrier-invoice-outcome")]
    public async Task AResetWhileASubscriptionThatRecordsIntoTheErpHoldsAMessage_IsRefusedWith409NamingIt_AndDeletesNothing_UntilItIsEmpty(string subscription)
    {
        await RecordOneOfEveryKindAsync();
        var held = await HeldAsync();
        Fixture.Subscriptions.Hold(subscription);

        using var refused = await ResetAsync();
        using var client = Fixture.ApiAs(ErpFixture.ResetRole);
        var readiness = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>(new Uri("/v1/reset", UriKind.Relative), Ct);

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await BodyOfAsync(refused);
        problem["code"]!.GetValue<string>().Should().Be("SubscriptionBusy");
        problem["detail"]!.GetValue<string>().Should().Contain(subscription);
        readiness!["busySubscription"]!.GetValue<string>().Should().Be(subscription);
        (await HeldAsync()).Should().Be(held);

        Fixture.Subscriptions.Empty();

        (await ResetSucceedsAsync())["Shipper invoices"].Should().Be(1);
    }

    [Fact]
    public async Task AResetWhileTheOutboxHoldsAnUnpublishedEvent_IsRefusedWith409NamingTheOutbox_AndDeletesNothing_UntilItIsPublished()
    {
        await RecordAsync(Brouwer, "TO-0000401");
        await Fixture.PublishedSettledAsync(Ct);
        IReadOnlyList<Application.ShipperInvoices.ShipperInvoiceView> invoices;
        using (Fixture.HoldOutbox())
        {
            // Issuing the invoice writes ShipperInvoiceIssued to the outbox, which stays there.
            await InvoiceAsync(Brouwer);

            using var refused = await ResetAsync();

            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await BodyOfAsync(refused);
            problem["code"]!.GetValue<string>().Should().Be("SubscriptionBusy");
            problem["detail"]!.GetValue<string>().Should().Contain("outbox");
            invoices = await InvoicesAsync();
        }

        invoices.Should().ContainSingle("nothing was deleted");
        await Fixture.PublishedSettledAsync(Ct);

        (await ResetSucceedsAsync())["Shipper invoices"].Should().Be(1);
    }
}
