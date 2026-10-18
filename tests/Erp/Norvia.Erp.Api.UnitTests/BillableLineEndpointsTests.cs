using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.BillableLines;
using Norvia.Erp.Application;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.BillableLines.Record;
using Norvia.Erp.Domain.BillableLines;

namespace Norvia.Erp.Api.UnitTests;

public sealed class BillableLineEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public BillableLineEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<RecordBillableLineResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<RecordBillableLineResult>>>>()(_mediator));

    private static BillableLineView View() => new(
        "TO-0000123", "SHP-0001", "PO-77", "SH-0000123", DateTimeOffset.UnixEpoch,
        new FreightChargeView("EUR", 100.50m, [new ChargeLineView("Freight", 100.50m)], "RC-0001", 2),
        false, "intake-1", "billing-1", DateTimeOffset.UnixEpoch, "Waiting");

    private void Answer(Result<RecordBillableLineResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<RecordBillableLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Record_AnInvalidEvent_AnswersAValidationProblemWithoutRecording()
    {
        var result = await BillableLineEndpoints.RecordAsync(null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<RecordBillableLineResult>>(default!);
    }

    [Fact]
    public async Task Record_ANewLine_AnswersCreated()
    {
        Answer(new RecordBillableLineResult(View(), Repeat: false));

        var result = await BillableLineEndpoints.RecordAsync(Request(), _retrier, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<RecordBillableLineResult>>().Subject;
        created.Location.Should().Be("/v1/billable-lines/TO-0000123");
        created.Value!.Repeat.Should().BeFalse();
    }

    [Fact]
    public async Task Record_ARepeat_AnswersOkWithTheOriginalLineFlaggedAsARepeat()
    {
        Answer(new RecordBillableLineResult(View(), Repeat: true));

        var result = await BillableLineEndpoints.RecordAsync(Request(), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<RecordBillableLineResult>>().Which.Value!.Repeat.Should().BeTrue();
    }

    [Theory]
    [InlineData(BillableLineRefusalCode.ShipperNotFound, 422)]
    [InlineData(BillableLineRefusalCode.BillableLineConflict, 409)]
    [InlineData(BillableLineRefusalCode.FreightChargeWithoutChargeLines, 400)]
    [InlineData(BillableLineRefusalCode.FreightChargeTotalMismatch, 400)]
    public async Task Record_ARefusal_AnswersAProblemWithItsStableCodeAndCorrelationId(BillableLineRefusalCode code, int status)
    {
        Answer(BillableLineRefused.Because<RecordBillableLineResult>(new BillableLineRefusal(code, "readable message")));

        var result = await BillableLineEndpoints.RecordAsync(Request(), _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Detail.Should().Be("readable message");
        problem.ProblemDetails.Extensions["code"].Should().Be(code.ToString());
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("billing-1");
    }

    private static TransportOrderBillableRequest Request() => new(
        Guid.NewGuid(), "TransportOrderBillable", 1, DateTimeOffset.UnixEpoch, "billing-1", "intake-1", "TO-0000123", "SHP-0001", "PO-77", "SH-0000123",
        DateTimeOffset.UnixEpoch,
        new FreightChargeRequest("EUR", 100.50m, [new ChargeLineRequest(ChargeLineType.Freight, 100.50m)], new RateCardVersionRequest("RC-0001", 2)),
        false);
}
