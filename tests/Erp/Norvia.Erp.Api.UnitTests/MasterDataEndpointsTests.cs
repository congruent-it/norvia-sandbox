using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.MasterData;
using Norvia.Erp.Application;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Carriers.List;
using Norvia.Erp.Application.MasterData.Carriers.Save;
using Norvia.Erp.Application.MasterData.Shippers.List;
using Norvia.Erp.Application.MasterData.Shippers.Save;

namespace Norvia.Erp.Api.UnitTests;

public sealed class MasterDataEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public MasterDataEndpointsTests()
    {
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<SaveShipperResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<SaveShipperResult>>>>()(_mediator));
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<SaveCarrierResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<SaveCarrierResult>>>>()(_mediator));
    }

    private void Answer(Result<SaveShipperResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<SaveShipperCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private void Answer(Result<SaveCarrierResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<SaveCarrierCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public async Task SaveShipper_ANewShipper_AnswersCreated()
    {
        Answer(new SaveShipperResult(new ShipperView("SHP-9", "New", true, 1), MasterDataSaveOutcome.Created));

        var result = await MasterDataEndpoints.SaveShipperAsync("SHP-9", new SaveShipperRequest("New", true), null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtRoute<SaveShipperResult>>().Which.Should().Match<CreatedAtRoute<SaveShipperResult>>(created => created.RouteName == "SaveShipper" && (string?)created.RouteValues["id"] == "SHP-9");
    }

    [Theory]
    [InlineData(MasterDataSaveOutcome.Updated)]
    [InlineData(MasterDataSaveOutcome.Unchanged)]
    public async Task SaveShipper_AnExistingShipper_AnswersOk(MasterDataSaveOutcome outcome)
    {
        Answer(new SaveShipperResult(new ShipperView("SHP-9", "New", true, 2), outcome));

        var result = await MasterDataEndpoints.SaveShipperAsync("SHP-9", new SaveShipperRequest("New", true), null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<SaveShipperResult>>();
    }

    [Fact]
    public async Task SaveShipper_ARefusal_AnswersABadRequestWithItsStableCode()
    {
        Answer(Result<SaveShipperResult>.Invalid(new ValidationError("Shipper", "needs a name", "MasterDataFieldRequired", ValidationSeverity.Error)));

        var result = await MasterDataEndpoints.SaveShipperAsync("SHP-9", new SaveShipperRequest("", true), null, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataFieldRequired");
    }

    [Fact]
    public async Task SaveCarrier_ANewCarrier_AnswersCreated()
    {
        Answer(new SaveCarrierResult(new CarrierView("CAR-9", "New", true, 1), MasterDataSaveOutcome.Created));

        var result = await MasterDataEndpoints.SaveCarrierAsync("CAR-9", new SaveCarrierRequest("New", true), null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtRoute<SaveCarrierResult>>().Which.Should().Match<CreatedAtRoute<SaveCarrierResult>>(created => created.RouteName == "SaveCarrier" && (string?)created.RouteValues["id"] == "CAR-9");
    }

    [Theory]
    [InlineData(MasterDataSaveOutcome.Updated)]
    [InlineData(MasterDataSaveOutcome.Unchanged)]
    public async Task SaveCarrier_AnExistingCarrier_AnswersOk(MasterDataSaveOutcome outcome)
    {
        Answer(new SaveCarrierResult(new CarrierView("CAR-9", "New", true, 2), outcome));

        var result = await MasterDataEndpoints.SaveCarrierAsync("CAR-9", new SaveCarrierRequest("New", true), null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<SaveCarrierResult>>();
    }

    [Fact]
    public async Task SaveCarrier_ARefusal_AnswersABadRequestWithItsStableCode()
    {
        Answer(Result<SaveCarrierResult>.Invalid(new ValidationError("Carrier", "needs a name", "MasterDataFieldRequired", ValidationSeverity.Error)));

        var result = await MasterDataEndpoints.SaveCarrierAsync("CAR-9", new SaveCarrierRequest("", true), null, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataFieldRequired");
    }
}
