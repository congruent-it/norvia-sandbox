using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.ShipperInvoices;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes;
using Norvia.Erp.Application.CreditNotes.Issue;
using Norvia.Erp.Application.ShipperInvoices;
using Norvia.Erp.Application.ShipperInvoices.Issue;

namespace Norvia.Erp.Api.UnitTests;

/// <summary>Invoice now and a Credit note through the ERP API: the actions the ERP UI has, with the same commands.</summary>
public sealed class InvoicingActionEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public InvoicingActionEndpointsTests()
    {
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<IssueShipperInvoiceResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<IssueShipperInvoiceResult>>>>()(_mediator));
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<CreditNoteView>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<CreditNoteView>>>>()(_mediator));
    }

    private static ShipperInvoiceView Invoice() => new(
        "INV-2026-000001", new DateOnly(2026, 10, 19), new ShipperOnInvoiceView("SHP-0001", "Brouwer Dranken"),
        [new ShipperInvoiceLineView("TO-0000001", "SIM-1-1", "SH-0000001", new DateOnly(2026, 10, 19), [new InvoiceChargeLineView("FREIGHT", 80m)], 80m)],
        "EUR", 80m, 16.80m, 96.80m, DateTimeOffset.UnixEpoch, "corr-1", "Issued");

    private static CreditNoteView Note() => new(
        "CN-2026-000001", new DateOnly(2026, 10, 19), new ShipperOnInvoiceView("SHP-0001", "Brouwer Dranken"), "INV-2026-000001", "TO-0000001", "SIM-1-1", "SH-0000001",
        new DateOnly(2026, 10, 19), "EUR", 20m, 4.20m, 24.20m, "Rate Card error", DateTimeOffset.UnixEpoch, "corr-2", "Issued");

#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
    private void Answer(Result<IssueShipperInvoiceResult> result) =>
        _mediator.Send(Arg.Any<IssueShipperInvoiceCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));

    private void Answer(Result<CreditNoteView> result) =>
        _mediator.Send(Arg.Any<IssueCreditNoteCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012

    [Fact]
    public void ManageRole_IsTheRoleOnlyTheSimulatorHolds() => ShipperInvoiceEndpoints.ManageRole.Should().Be("ShipperInvoices.Manage");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task InvoiceNow_WithoutAShipper_AnswersAValidationProblemWithoutRunning(string? shipperId)
    {
        var result = await ShipperInvoiceEndpoints.InvoiceNowAsync(new InvoiceNowRequest(shipperId), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<IssueShipperInvoiceResult>>(default!);
    }

    [Fact]
    public async Task InvoiceNow_WithoutABody_AnswersAValidationProblem()
    {
        var result = await ShipperInvoiceEndpoints.InvoiceNowAsync(null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Fact]
    public async Task InvoiceNow_AShipperWithWaitingLines_AnswersOkWithTheInvoiceIssued()
    {
        var issued = new IssueShipperInvoiceResult(Invoice());
        Answer(issued);

        var result = await ShipperInvoiceEndpoints.InvoiceNowAsync(new InvoiceNowRequest("SHP-0001"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<IssueShipperInvoiceResult>>().Which.Value.Should().Be(issued);
        await _mediator.Received(1).Send(new IssueShipperInvoiceCommand("SHP-0001"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvoiceNow_AShipperWithNothingWaiting_AnswersOkWithoutAnInvoice()
    {
        Answer(new IssueShipperInvoiceResult(null));

        var result = await ShipperInvoiceEndpoints.InvoiceNowAsync(new InvoiceNowRequest("SHP-0001"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<IssueShipperInvoiceResult>>().Which.Value!.Invoice.Should().BeNull();
    }

    [Fact]
    public async Task InvoiceNow_AnUnknownShipper_AnswersAProblemWithItsStableCode()
    {
        Answer(Result<IssueShipperInvoiceResult>.NotFound("The ERP has no Shipper SHP-9999."));

        var result = await ShipperInvoiceEndpoints.InvoiceNowAsync(new InvoiceNowRequest("SHP-9999"), _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("ShipperNotFound");
    }

    [Fact]
    public async Task IssueCreditNote_ACompleteRequest_IssuesItAndAnswersCreated()
    {
        var note = Note();
        Answer(note);

        var result = await ShipperInvoiceEndpoints.IssueCreditNoteAsync(
            "INV-2026-000001", new IssueCreditNoteRequest("TO-0000001", 20m, "Rate Card error"), _retrier, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<CreditNoteView>>().Subject;
        created.Value.Should().Be(note);
        created.Location.Should().Be("/v1/credit-notes/CN-2026-000001");
        await _mediator.Received(1).Send(new IssueCreditNoteCommand("INV-2026-000001", "TO-0000001", 20m, "Rate Card error"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueCreditNote_WithoutTheLineOrTheAmount_AnswersAValidationProblemNamingBoth()
    {
        var result = await ShipperInvoiceEndpoints.IssueCreditNoteAsync("INV-2026-000001", new IssueCreditNoteRequest(null, null, "Reason"), _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ValidationProblem>().Subject;
        problem.ProblemDetails.Errors.Keys.Should().BeEquivalentTo(nameof(IssueCreditNoteRequest.TransportOrderNumber), nameof(IssueCreditNoteRequest.AmountWithoutVat));
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<CreditNoteView>>(default!);
    }

    [Fact]
    public async Task IssueCreditNote_WithoutABody_AnswersAValidationProblem()
    {
        var result = await ShipperInvoiceEndpoints.IssueCreditNoteAsync("INV-2026-000001", null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Theory]
    [InlineData("ShipperInvoiceNotFound", 404)]
    [InlineData("InvoiceLineNotFound", 404)]
    [InlineData("CreditNoteReasonRequired", 400)]
    [InlineData("CreditNoteReasonTooLong", 400)]
    [InlineData("CreditNoteAmountInvalid", 400)]
    [InlineData("CreditNoteAboveLineTotal", 422)]
    public async Task IssueCreditNote_ARefusal_AnswersAProblemWithItsStableCodeAndTheStatusOfTheRuleBroken(string code, int status)
    {
        Answer(Result<CreditNoteView>.Invalid(new ValidationError("CreditNote", "Refused.", code, ValidationSeverity.Error)));

        var result = await ShipperInvoiceEndpoints.IssueCreditNoteAsync(
            "INV-2026-000001", new IssueCreditNoteRequest("TO-0000001", 500m, "Reason"), _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions["code"].Should().Be(code);
        problem.ProblemDetails.Extensions["correlationId"].Should().NotBeNull();
    }
}
