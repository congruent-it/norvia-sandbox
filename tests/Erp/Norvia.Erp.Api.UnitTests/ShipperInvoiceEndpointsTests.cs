using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.ShipperInvoices;
using Norvia.Erp.Application;
using Norvia.Erp.Application.ShipperInvoices.ReportSent;

namespace Norvia.Erp.Api.UnitTests;

public sealed class ShipperInvoiceEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public ShipperInvoiceEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<ShipperInvoiceReportResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<ShipperInvoiceReportResult>>>>()(_mediator));

    private void Answer(Result<ShipperInvoiceReportResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReportShipperInvoiceSentCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ReportSent_WithoutACorrelationId_AnswersAValidationProblemWithoutReporting(string? correlationId)
    {
        var result = await ShipperInvoiceEndpoints.ReportSentAsync("INV-2026-000001", correlationId, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<ShipperInvoiceReportResult>>(default!);
    }

    [Fact]
    public async Task ReportSent_AnIssuedInvoice_AnswersOkWithTheSentInvoice()
    {
        var sent = new ShipperInvoiceReportResult("INV-2026-000001", "Sent", DateTimeOffset.UnixEpoch);
        Answer(sent);

        var result = await ShipperInvoiceEndpoints.ReportSentAsync("INV-2026-000001", "corr-1", _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ShipperInvoiceReportResult>>().Which.Value.Should().Be(sent);
        await _mediator.Received(1).Send(new ReportShipperInvoiceSentCommand("INV-2026-000001", "corr-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReportSent_ARepeat_AnswersOkFlaggedAsIgnored()
    {
        Answer(new ShipperInvoiceReportResult("INV-2026-000001", "Sent", DateTimeOffset.UnixEpoch, Ignored: true));

        var result = await ShipperInvoiceEndpoints.ReportSentAsync("INV-2026-000001", "corr-1", _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ShipperInvoiceReportResult>>().Which.Value!.Ignored.Should().BeTrue();
    }

    [Fact]
    public async Task ReportSent_AnUnknownInvoice_AnswersAProblemWithItsStableCodeAndCorrelationId()
    {
        Answer(Result<ShipperInvoiceReportResult>.Invalid(new ValidationError("ShipperInvoice", "The ERP has no Shipper invoice INV-2026-999999.", "ShipperInvoiceNotFound", ValidationSeverity.Error)));

        var result = await ShipperInvoiceEndpoints.ReportSentAsync("INV-2026-999999", "corr-1", _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("ShipperInvoiceNotFound");
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("corr-1");
    }
}
