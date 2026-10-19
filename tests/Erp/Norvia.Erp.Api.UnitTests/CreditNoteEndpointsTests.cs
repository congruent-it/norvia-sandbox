using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.ShipperInvoices;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CreditNotes.ReportNotDelivered;
using Norvia.Erp.Application.CreditNotes.ReportSent;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CreditNoteEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public CreditNoteEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<CreditNoteReportResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<CreditNoteReportResult>>>>()(_mediator));

    private static Result<CreditNoteReportResult> NotFound() =>
        Result<CreditNoteReportResult>.Invalid(new ValidationError("CreditNote", "The ERP has no Credit note CN-2026-999999.", "CreditNoteNotFound", ValidationSeverity.Error));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task ReportSent_WithoutACorrelationId_AnswersAValidationProblemWithoutReporting(string? correlationId)
    {
        var result = await CreditNoteEndpoints.ReportSentAsync("CN-2026-000001", correlationId, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<CreditNoteReportResult>>(default!);
    }

    [Fact]
    public async Task ReportSent_AnIssuedCreditNote_AnswersOkWithTheSentCreditNote()
    {
        var sent = new CreditNoteReportResult("CN-2026-000001", "Sent", DateTimeOffset.UnixEpoch);
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReportCreditNoteSentCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Result<CreditNoteReportResult>>(sent));
#pragma warning restore CA2012

        var result = await CreditNoteEndpoints.ReportSentAsync("CN-2026-000001", "corr-1", _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CreditNoteReportResult>>().Which.Value.Should().Be(sent);
        await _mediator.Received(1).Send(new ReportCreditNoteSentCommand("CN-2026-000001", "corr-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSent_AnUnknownCreditNote_AnswersAProblemWithItsStableCodeAndCorrelationId()
    {
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<ReportCreditNoteSentCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(NotFound()));
#pragma warning restore CA2012

        var result = await CreditNoteEndpoints.ReportSentAsync("CN-2026-999999", "corr-1", _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("CreditNoteNotFound");
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("corr-1");
    }

    [Theory]
    [InlineData(null, "cause")]
    [InlineData(" ", "cause")]
    [InlineData("corr-1", null)]
    [InlineData("corr-1", " ")]
    public async Task ReportNotDelivered_WithoutACorrelationIdOrACause_AnswersAValidationProblemWithoutReporting(string? correlationId, string? message)
    {
        var result = await CreditNoteEndpoints.ReportNotDeliveredAsync(
            "CN-2026-000001", correlationId, new ShipperInvoiceNotDeliveredRequest(503, message), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<CreditNoteReportResult>>(default!);
    }

    [Fact]
    public async Task ReportNotDelivered_AnIssuedCreditNote_AnswersOkWithTheNotDeliveredCreditNote()
    {
        var reported = new CreditNoteReportResult("CN-2026-000001", "NotDelivered", null);
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<ReportCreditNoteNotDeliveredCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<Result<CreditNoteReportResult>>(reported));
#pragma warning restore CA2012

        var result = await CreditNoteEndpoints.ReportNotDeliveredAsync(
            "CN-2026-000001", "corr-1", new ShipperInvoiceNotDeliveredRequest(503, "Overloaded"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CreditNoteReportResult>>().Which.Value.Should().Be(reported);
        await _mediator.Received(1).Send(new ReportCreditNoteNotDeliveredCommand("CN-2026-000001", "corr-1", 503, "Overloaded"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportNotDelivered_AnUnknownCreditNote_AnswersAProblemWithItsStableCode()
    {
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<ReportCreditNoteNotDeliveredCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(NotFound()));
#pragma warning restore CA2012

        var result = await CreditNoteEndpoints.ReportNotDeliveredAsync(
            "CN-2026-999999", "corr-1", new ShipperInvoiceNotDeliveredRequest(500, "cause"), _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("CreditNoteNotFound");
    }
}
