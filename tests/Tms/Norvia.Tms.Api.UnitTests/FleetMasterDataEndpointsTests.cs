using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Api.Fleet;
using Norvia.Tms.Application.Fleet.ReplaceDriver;
using Norvia.Tms.Application.Fleet.ReplaceVehicle;
using NSubstitute;

namespace Norvia.Tms.Api.UnitTests;

public sealed class FleetMasterDataEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    [Fact]
    public async Task ReplaceVehicle_AnAppliedOrStaleRevision_AnswersOkAndSendsTheWholeRecord()
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<ReplaceVehicleCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Result<ReplaceVehicleResult>>(new ReplaceVehicleResult(Applied: false, 3)));
#pragma warning restore CA2012

        var result = await VehicleEndpoints.ReplaceAsync("VEH-1", new ReplaceVehicleRequest("VN-1", "Van", 1200, 4, true, 2), _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ReplaceVehicleResult>>().Which.Value.Should().Be(new ReplaceVehicleResult(false, 3));
        await _mediator.Received().Send(new ReplaceVehicleCommand("VEH-1", "VN-1", "Van", 1200, 4, true, 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplaceVehicle_AnUnreadableRecord_AnswersABadRequestWithItsCode()
    {
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<ReplaceVehicleCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(
            Result<ReplaceVehicleResult>.Invalid(new ValidationError("Vehicle", "unreadable", "MasterDataUnreadable", ValidationSeverity.Error))));
#pragma warning restore CA2012

        var result = await VehicleEndpoints.ReplaceAsync("VEH-1", new ReplaceVehicleRequest("", "Van", 1, 0, true, 0), _mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataUnreadable");
    }

    [Fact]
    public async Task ReplaceDriver_AnAppliedOrStaleRevision_AnswersOk()
    {
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<ReplaceDriverCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<Result<ReplaceDriverResult>>(new ReplaceDriverResult(Applied: true, 2)));
#pragma warning restore CA2012

        var result = await DriverEndpoints.ReplaceAsync("DRV-1", new ReplaceDriverRequest("Anouk", ["B"], true, 2), _mediator, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<ReplaceDriverResult>>().Which.Value.Should().Be(new ReplaceDriverResult(true, 2));
    }

    [Fact]
    public async Task ReplaceDriver_AnUnreadableRecord_AnswersABadRequestWithItsCode()
    {
#pragma warning disable CA2012
        _mediator.Send(Arg.Any<ReplaceDriverCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(
            Result<ReplaceDriverResult>.Invalid(new ValidationError("Driver", "unreadable", "MasterDataUnreadable", ValidationSeverity.Error))));
#pragma warning restore CA2012

        var result = await DriverEndpoints.ReplaceAsync("DRV-1", new ReplaceDriverRequest("", ["X"], true, 0), _mediator, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Extensions["code"].Should().Be("MasterDataUnreadable");
    }

    [Fact]
    public void MapVehiclesAndDrivers_RegisterTheirReplaceRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddApiVersioning();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(_mediator);
        var app = builder.Build();

        app.MapVehicles();
        app.MapDrivers();

        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToList();
        routes.Should().Contain("/v{version:apiVersion}/vehicles/{id}").And.Contain("/v{version:apiVersion}/drivers/{id}");
    }
}
