using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.CarrierCosts;
using Norvia.Erp.Application;
using Norvia.Erp.Application.BillableLines;
using Norvia.Erp.Application.CarrierCosts;
using Norvia.Erp.Application.CarrierCosts.Record;
using Norvia.Erp.Domain.BillableLines;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CarrierCostEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public CarrierCostEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<RecordCarrierCostResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<RecordCarrierCostResult>>>>()(_mediator));

    private static CarrierCostView View() => new(
        "CB-0000123", "TRX-998", "CAR-0001", "SH-0000123", "TO-0000123", "EUR", 70.50m, [new ChargeLineView("Freight", 70.50m)],
        new CarrierCostSourceView("CarrierRateCard", "CRC-0001", 2, null), DateTimeOffset.UnixEpoch, "delivered-1", DateTimeOffset.UnixEpoch, "Open");

    private void Answer(Result<RecordCarrierCostResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<RecordCarrierCostCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task Record_AnInvalidEvent_AnswersAValidationProblemWithoutRecording()
    {
        var result = await CarrierCostEndpoints.RecordAsync(null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
        await _retrier.DidNotReceiveWithAnyArgs().RunAsync<Result<RecordCarrierCostResult>>(default!);
    }

    [Fact]
    public async Task Record_ANewCost_AnswersCreated()
    {
        Answer(new RecordCarrierCostResult(View(), Repeat: false));

        var result = await CarrierCostEndpoints.RecordAsync(CarrierCostIncurredValidatorTests.Valid, _retrier, CancellationToken.None);

        var created = result.Result.Should().BeOfType<Created<RecordCarrierCostResult>>().Subject;
        created.Location.Should().Be("/v1/carrier-costs/CB-0000123");
        created.Value!.Repeat.Should().BeFalse();
    }

    [Fact]
    public async Task Record_ARepeat_AnswersOkWithTheOriginalFlaggedAsARepeat()
    {
        Answer(new RecordCarrierCostResult(View(), Repeat: true));

        var result = await CarrierCostEndpoints.RecordAsync(CarrierCostIncurredValidatorTests.Valid, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<RecordCarrierCostResult>>().Which.Value!.Repeat.Should().BeTrue();
    }

    [Theory]
    [InlineData(CarrierCostRefusalCode.CarrierNotFound, 422)]
    [InlineData(CarrierCostRefusalCode.CarrierCostConflict, 409)]
    [InlineData(CarrierCostRefusalCode.CarrierCostWithoutChargeLines, 400)]
    [InlineData(CarrierCostRefusalCode.CarrierCostTotalMismatch, 400)]
    public async Task Record_ARefusal_AnswersAProblemWithItsStableCodeAndCorrelationId(CarrierCostRefusalCode code, int status)
    {
        Answer(CarrierCostRefused.Because<RecordCarrierCostResult>(new CarrierCostRefusal(code, "readable message")));

        var result = await CarrierCostEndpoints.RecordAsync(CarrierCostIncurredValidatorTests.Valid, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Detail.Should().Be("readable message");
        problem.ProblemDetails.Extensions["code"].Should().Be(code.ToString());
        problem.ProblemDetails.Extensions["correlationId"].Should().Be("delivered-1");
    }
}
