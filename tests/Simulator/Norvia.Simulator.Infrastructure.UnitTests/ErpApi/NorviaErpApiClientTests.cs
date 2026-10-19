using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Simulator.Domain.Log;
using Norvia.Simulator.Infrastructure.ErpApi;
using Norvia.Simulator.Infrastructure.PartnerApi;
using Norvia.Simulator.Infrastructure.UnitTests.PartnerApi;

namespace Norvia.Simulator.Infrastructure.UnitTests.ErpApi;

public sealed class NorviaErpApiClientTests : IDisposable
{
    private readonly StubHandler _erp = new();
    private readonly IErpTokens _tokens = Substitute.For<IErpTokens>();
    private readonly FakeLogger<NorviaErpApiClient> _logger = new();
    private readonly SimulatorActivities _activities = new("Norvia.Simulator.Web.Tests.Erp");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly NorviaErpApiClient _client;

    public NorviaErpApiClientTests()
    {
        _http = new HttpClient(_erp) { BaseAddress = new Uri("https://erp.example/") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("finance-token");
        _client = new NorviaErpApiClient(_http, _tokens, _activities, _logger);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == _activities.Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
        _http.Dispose();
        _erp.Dispose();
    }

    [Fact]
    public async Task InvoiceNow_PostsTheShipperAsTheSimulator_InASpanNamedLikeTheErpUis()
    {
        _erp.Answer(HttpStatusCode.OK, """{"invoice":{"invoiceNumber":"INV-2026-000001","correlationId":"corr-1","totalWithoutVat":80.00,"lines":[{"transportOrderNumber":"TO-1","shipperReference":"SIM-1-1","total":80.00}]}}""");

        var answer = await _client.InvoiceNowAsync("SHP-0001", TestContext.Current.CancellationToken);

        _erp.Request!.Method.Should().Be(HttpMethod.Post);
        _erp.Request.RequestUri.Should().Be(new Uri("https://erp.example/v1/shipper-invoices"));
        _erp.Request.Headers.Authorization!.Parameter.Should().Be("finance-token");
        JsonNode.Parse(_erp.RequestBody!)!["shipperId"]!.GetValue<string>().Should().Be("SHP-0001");
        answer.Outcome.Should().Be(LogOutcome.InvoiceIssued);
        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Invoice now");
    }

    [Fact]
    public async Task InvoiceNow_IsLoggedWithTheTrackingKeysItKnows()
    {
        _erp.Answer(HttpStatusCode.OK, """{"invoice":{"invoiceNumber":"INV-2026-000001","correlationId":"corr-1","lines":[{"transportOrderNumber":"TO-1","shipperReference":"SIM-1-1","total":80.00}]}}""");

        await _client.InvoiceNowAsync("SHP-0001", TestContext.Current.CancellationToken);

        var scope = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject.Scopes.Should().ContainSingle().Subject;
        var keys = scope.Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Subject.ToDictionary(pair => pair.Key, pair => pair.Value);
        keys.Should().Contain("flow", "invoicing").And.Contain("outcome", "Succeeded").And.Contain("correlationId", "corr-1")
            .And.Contain("documentNumber", "INV-2026-000001").And.Contain("partyId", "SHP-0001");
    }

    [Fact]
    public async Task InvoiceNow_TheErpUnreachable_IsAnAnswer_NotAnError()
    {
        _erp.Fail(new HttpRequestException("down"));

        var answer = await _client.InvoiceNowAsync("SHP-0001", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(LogOutcome.InvoicingNotReceived);
        answer.Detail.Should().Contain("didn't reach the ERP API");
        _logger.Collector.GetSnapshot().Should().Contain(record => record.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task InvoiceNow_ARunCancelled_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _erp.Fail(new OperationCanceledException(cancelled.Token));

        var act = () => _client.InvoiceNowAsync("SHP-0001", cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IssueCreditNote_PostsTheLineTheAmountAndTheReason_OnTheInvoice()
    {
        _erp.Answer(HttpStatusCode.Created, """{"creditNoteNumber":"CN-2026-000001","correlationId":"corr-2"}""");

        var answer = await _client.IssueCreditNoteAsync("INV-2026-000001", "TO-0000001", 20.00m, "Rate Card error", TestContext.Current.CancellationToken);

        _erp.Request!.RequestUri.Should().Be(new Uri("https://erp.example/v1/shipper-invoices/INV-2026-000001/credit-notes"));
        _erp.Request.Headers.Authorization!.Parameter.Should().Be("finance-token");
        var sent = JsonNode.Parse(_erp.RequestBody!)!;
        (sent["transportOrderNumber"]!.GetValue<string>(), sent["amountWithoutVat"]!.GetValue<decimal>(), sent["reason"]!.GetValue<string>()).Should().Be(("TO-0000001", 20.00m, "Rate Card error"));
        answer.Should().Be(new Application.Ports.CreditNoteAnswer(LogOutcome.CreditNoteIssued, "corr-2", "CN-2026-000001", null));
        _spans.Should().ContainSingle().Which.OperationName.Should().Be("Issue Credit note");
    }

    [Fact]
    public async Task IssueCreditNote_ARefusal_IsLoggedAsRefusedWithItsCode()
    {
        _erp.Answer(HttpStatusCode.UnprocessableEntity, """{"code":"CreditNoteAboveLineTotal","correlationId":"corr-3"}""");

        var answer = await _client.IssueCreditNoteAsync("INV-2026-000001", "TO-0000001", 500m, "Reason", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(LogOutcome.InvoicingRefused);
        var scope = _logger.Collector.GetSnapshot().Should().ContainSingle().Subject.Scopes.Should().ContainSingle().Subject;
        var keys = scope.Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>().Subject.ToDictionary(pair => pair.Key, pair => pair.Value);
        keys.Should().Contain("outcome", "Refused").And.Contain("failureCode", "CreditNoteAboveLineTotal").And.Contain("correlationId", "corr-3");
    }

    [Fact]
    public async Task IssueCreditNote_TheErpUnreachable_IsAnAnswer_NotAnError()
    {
        _erp.Fail(new HttpRequestException("down"));

        var answer = await _client.IssueCreditNoteAsync("INV-2026-000001", "TO-0000001", 20m, "Reason", TestContext.Current.CancellationToken);

        answer.Outcome.Should().Be(LogOutcome.InvoicingNotReceived);
    }

    [Fact]
    public void TrackingKeys_AnythingElse_IsFailed_AndLeavesOutWhatDoesNotApply()
    {
        var keys = NorviaErpApiClient.TrackingKeysOf(LogOutcome.InvoicingNotReceived, null, null, "down", null);

        keys.Should().BeEquivalentTo(new Dictionary<string, object?> { ["flow"] = "invoicing", ["outcome"] = "Failed", ["failureCode"] = "InvoicingNotReceived" });
    }

    [Fact]
    public async Task Calls_WithoutTheirKeys_AreRefused()
    {
        var invoice = () => _client.InvoiceNowAsync(" ", TestContext.Current.CancellationToken);
        var credit = () => _client.IssueCreditNoteAsync("", "TO-1", 1m, "r", TestContext.Current.CancellationToken);
        var line = () => _client.IssueCreditNoteAsync("INV-1", "", 1m, "r", TestContext.Current.CancellationToken);

        await invoice.Should().ThrowAsync<ArgumentException>();
        await credit.Should().ThrowAsync<ArgumentException>();
        await line.Should().ThrowAsync<ArgumentException>();
    }
}
