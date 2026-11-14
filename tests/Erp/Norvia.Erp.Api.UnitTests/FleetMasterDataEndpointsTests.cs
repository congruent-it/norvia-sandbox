using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Norvia.Erp.Api.MasterData;
using Norvia.Erp.Application;
using Norvia.Erp.Application.MasterData;
using Norvia.Erp.Application.MasterData.Drivers;
using Norvia.Erp.Application.MasterData.Drivers.Save;
using Norvia.Erp.Application.MasterData.Vehicles;
using Norvia.Erp.Application.MasterData.Vehicles.Save;

namespace Norvia.Erp.Api.UnitTests;

public sealed class FleetMasterDataEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public FleetMasterDataEndpointsTests()
    {
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<SaveVehicleResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<SaveVehicleResult>>>>()(_mediator));
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<SaveDriverResult>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<SaveDriverResult>>>>()(_mediator));
    }

    private void Answer(Result<SaveVehicleResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<SaveVehicleCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private void Answer(Result<SaveDriverResult> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<SaveDriverCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private static readonly SaveVehicleRequest VehicleRequest = new("VN-1", "Van", 1200, 4, true);
    private static readonly SaveDriverRequest DriverRequest = new("Anouk", ["B"], true);

    [Fact]
    public async Task SaveVehicle_ANewVehicle_AnswersCreatedAndSendsTheWholeRecord()
    {
        Answer(new SaveVehicleResult(new VehicleView("VEH-9", "VN-1", "Van", 1200, 4, true, 1), MasterDataSaveOutcome.Created));

        var result = await MasterDataEndpoints.SaveVehicleAsync("VEH-9", VehicleRequest, null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtRoute<SaveVehicleResult>>().Which.Should().Match<CreatedAtRoute<SaveVehicleResult>>(created => created.RouteName == "SaveVehicle" && (string?)created.RouteValues["id"] == "VEH-9");
        await _mediator.Received().Send(new SaveVehicleCommand("VEH-9", "VN-1", "Van", 1200, 4, true), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(MasterDataSaveOutcome.Updated)]
    [InlineData(MasterDataSaveOutcome.Unchanged)]
    public async Task SaveVehicle_AnExistingVehicle_AnswersOk(MasterDataSaveOutcome outcome)
    {
        Answer(new SaveVehicleResult(new VehicleView("VEH-9", "VN-1", "Van", 1200, 4, true, 2), outcome));

        var result = await MasterDataEndpoints.SaveVehicleAsync("VEH-9", VehicleRequest, null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<SaveVehicleResult>>();
    }

    [Fact]
    public async Task SaveVehicle_ARefusal_AnswersABadRequestWithItsStableCode()
    {
        Answer(Result<SaveVehicleResult>.Invalid(new ValidationError("Vehicle", "bad type", "MasterDataFieldInvalid", ValidationSeverity.Error)));

        var result = await MasterDataEndpoints.SaveVehicleAsync("VEH-9", VehicleRequest, null, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataFieldInvalid");
    }

    [Fact]
    public async Task SaveDriver_ANewDriver_AnswersCreated()
    {
        Answer(new SaveDriverResult(new DriverView("DRV-9", "Anouk", ["B"], true, 1), MasterDataSaveOutcome.Created));

        var result = await MasterDataEndpoints.SaveDriverAsync("DRV-9", DriverRequest, null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<CreatedAtRoute<SaveDriverResult>>().Which.Should().Match<CreatedAtRoute<SaveDriverResult>>(created => created.RouteName == "SaveDriver" && (string?)created.RouteValues["id"] == "DRV-9");
    }

    [Theory]
    [InlineData(MasterDataSaveOutcome.Updated)]
    [InlineData(MasterDataSaveOutcome.Unchanged)]
    public async Task SaveDriver_AnExistingDriver_AnswersOk(MasterDataSaveOutcome outcome)
    {
        Answer(new SaveDriverResult(new DriverView("DRV-9", "Anouk", ["B"], true, 2), outcome));

        var result = await MasterDataEndpoints.SaveDriverAsync("DRV-9", DriverRequest, null, _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<SaveDriverResult>>();
    }

    [Fact]
    public async Task SaveDriver_ARefusal_AnswersABadRequestWithItsStableCode()
    {
        Answer(Result<SaveDriverResult>.Invalid(new ValidationError("Driver", "needs a name", "MasterDataFieldRequired", ValidationSeverity.Error)));

        var result = await MasterDataEndpoints.SaveDriverAsync("DRV-9", DriverRequest, null, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataFieldRequired");
    }
}
