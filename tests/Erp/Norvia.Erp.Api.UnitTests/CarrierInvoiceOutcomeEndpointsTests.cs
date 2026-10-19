using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.CarrierInvoices;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices.ReportNotDelivered;
using Norvia.Erp.Application.CarrierInvoices.ReportTold;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CarrierInvoiceOutcomeEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public CarrierInvoiceOutcomeEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<CarrierInvoiceReportResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<CarrierInvoiceReportResult>>>>()(_mediator));

    private static Result<CarrierInvoiceReportResult> NotFound() =>
        Result<CarrierInvoiceReportResult>.Invalid(
            new ValidationError("CarrierInvoice", "The ERP has no Carrier invoice VDT-9 of Carrier CAR-0001.", "CarrierInvoiceNotFound", ValidationSeverity.Error));

    private void AnswerTold(Result<CarrierInvoiceReportResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReportCarrierInvoiceToldCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private void AnswerNotDelivered(Result<CarrierInvoiceReportResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReportCarrierInvoiceOutcomeNotDeliveredCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public void Report_IsGuardedByTheReportRoleOnly() => CarrierInvoiceEndpoints.ReportRole.Should().Be("CarrierInvoices.Report");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReportTold_WithoutACorrelationId_AnswersAValidationProblemWithoutReporting(string? correlationId)
    {
        var result = await CarrierInvoiceEndpoints.ReportToldAsync("CAR-0001", "VDT-1", correlationId, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<CarrierInvoiceReportResult>>(default!);
    }

    [Fact]
    public async Task ReportTold_AnApprovedInvoice_AnswersOkWithTheTimeTheCarrierWasTold()
    {
        var told = new CarrierInvoiceReportResult("CAR-0001", "VDT-1", DateTimeOffset.UnixEpoch);
        AnswerTold(told);

        var result = await CarrierInvoiceEndpoints.ReportToldAsync("CAR-0001", "VDT-1", "corr-1", _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierInvoiceReportResult>>().Which.Value.Should().Be(told);
        await _mediator.Received(1).Send(new ReportCarrierInvoiceToldCommand("CAR-0001", "VDT-1", "corr-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportTold_ARepeat_AnswersOkFlaggedAsIgnored()
    {
        AnswerTold(new CarrierInvoiceReportResult("CAR-0001", "VDT-1", DateTimeOffset.UnixEpoch, Ignored: true));

        var result = await CarrierInvoiceEndpoints.ReportToldAsync("CAR-0001", "VDT-1", "corr-1", _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierInvoiceReportResult>>().Which.Value!.Ignored.Should().BeTrue();
    }

    [Fact]
    public async Task ReportTold_AnUnknownInvoice_AnswersAProblemWithItsStableCodeAndCorrelationId()
    {
        AnswerTold(NotFound());

        var result = await CarrierInvoiceEndpoints.ReportToldAsync("CAR-0001", "VDT-9", "corr-1", _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("CarrierInvoiceNotFound");
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("corr-1");
    }

    [Theory]
    [InlineData(null, "cause")]
    [InlineData(" ", "cause")]
    [InlineData("corr-1", null)]
    [InlineData("corr-1", " ")]
    public async Task ReportNotDelivered_WithoutACorrelationIdOrACause_AnswersAValidationProblemWithoutReporting(string? correlationId, string? message)
    {
        var result = await CarrierInvoiceEndpoints.ReportNotDeliveredAsync(
            "CAR-0001", "VDT-1", correlationId, new CarrierInvoiceOutcomeNotDeliveredRequest(503, message), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<CarrierInvoiceReportResult>>(default!);
    }

    [Fact]
    public async Task ReportNotDelivered_AnApprovedInvoice_AnswersOk()
    {
        var reported = new CarrierInvoiceReportResult("CAR-0001", "VDT-1", null);
        AnswerNotDelivered(reported);

        var result = await CarrierInvoiceEndpoints.ReportNotDeliveredAsync(
            "CAR-0001", "VDT-1", "corr-1", new CarrierInvoiceOutcomeNotDeliveredRequest(503, "Overloaded"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierInvoiceReportResult>>().Which.Value.Should().Be(reported);
        await _mediator.Received(1).Send(new ReportCarrierInvoiceOutcomeNotDeliveredCommand("CAR-0001", "VDT-1", "corr-1", 503, "Overloaded"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportNotDelivered_ALateReport_AnswersOkFlaggedAsIgnored()
    {
        AnswerNotDelivered(new CarrierInvoiceReportResult("CAR-0001", "VDT-1", DateTimeOffset.UnixEpoch, Ignored: true));

        var result = await CarrierInvoiceEndpoints.ReportNotDeliveredAsync(
            "CAR-0001", "VDT-1", "corr-1", new CarrierInvoiceOutcomeNotDeliveredRequest(null, "timed out"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierInvoiceReportResult>>().Which.Value!.Ignored.Should().BeTrue();
    }

    [Fact]
    public async Task ReportNotDelivered_AnUnknownInvoice_AnswersAProblemWithItsStableCode()
    {
        AnswerNotDelivered(NotFound());

        var result = await CarrierInvoiceEndpoints.ReportNotDeliveredAsync(
            "CAR-0001", "VDT-9", "corr-1", new CarrierInvoiceOutcomeNotDeliveredRequest(500, "cause"), _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("CarrierInvoiceNotFound");
    }
}
