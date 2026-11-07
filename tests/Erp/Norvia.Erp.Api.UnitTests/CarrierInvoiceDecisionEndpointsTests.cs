using Ardalis.Result;
using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Agents;
using Norvia.Erp.Api.CarrierInvoices;
using Norvia.Erp.Application;
using Norvia.Erp.Application.CarrierInvoices;
using Norvia.Erp.Application.CarrierInvoices.Decide;
using Norvia.Erp.Domain.Carriers;

namespace Norvia.Erp.Api.UnitTests;

public sealed class CarrierInvoiceDecisionEndpointsTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IConflictRetrier _retrier = Substitute.For<IConflictRetrier>();

    public CarrierInvoiceDecisionEndpointsTests() =>
        _retrier.RunAsync(Arg.Any<Func<IMediator, Task<Result<CarrierInvoiceView>>>>())
            .Returns(call => call.Arg<Func<IMediator, Task<Result<CarrierInvoiceView>>>>()(_mediator));

    private static Result<CarrierInvoiceView> Refused(string code) =>
        Result<CarrierInvoiceView>.Invalid(new ValidationError("CarrierInvoice", $"Refused: {code}.", code, ValidationSeverity.Error));

    private void AnswerAccept(Result<CarrierInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<AcceptCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    private void AnswerReject(Result<CarrierInvoiceView> result)
    {
#pragma warning disable CA2012 // NSubstitute configures the call by reading its return value, which is never consumed.
        _mediator.Send(Arg.Any<RejectCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(result));
#pragma warning restore CA2012
    }

    [Fact]
    public void Decisions_AreGuardedByTheManageRole() => CarrierInvoiceEndpoints.ManageRole.Should().Be("CarrierInvoices.Manage");

    [Fact]
    public async Task Accept_RunsTheUiCommandWithTheAdjustedAmountAndRemark()
    {
        var invoice = (CarrierInvoiceView)null!;
        AnswerAccept(Result<CarrierInvoiceView>.Success(invoice));

        var result = await CarrierInvoiceEndpoints.AcceptLineAsync(
            "CAR-0001", "VDT-1", "BK-1", new AcceptCarrierInvoiceLineRequest(95m, "Waiting time"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierInvoiceView>>();
        await _mediator.Received(1).Send(
            new AcceptCarrierInvoiceLineCommand(new CarrierId("CAR-0001"), "VDT-1", "BK-1", 95m, "Waiting time"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_WithoutABody_AcceptsAtTheInvoicedAmount()
    {
        AnswerAccept(Result<CarrierInvoiceView>.Success((CarrierInvoiceView)null!));

        await CarrierInvoiceEndpoints.AcceptLineAsync("CAR-0001", "VDT-1", "BK-1", null, _retrier, CancellationToken.None);

        await _mediator.Received(1).Send(
            new AcceptCarrierInvoiceLineCommand(new CarrierId("CAR-0001"), "VDT-1", "BK-1", null, null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_NamingTheAgentAndRunItActsOn_PassesThemToTheCommandForTheTrace()
    {
        AnswerAccept(Result<CarrierInvoiceView>.Success((CarrierInvoiceView)null!));

        await CarrierInvoiceEndpoints.AcceptLineAsync(
            "CAR-0001", "VDT-1", "BK-1", new AcceptCarrierInvoiceLineRequest(null, null, " finance-matching ", " run-1 "), _retrier, CancellationToken.None);

        await _mediator.Received(1).Send(
            new AcceptCarrierInvoiceLineCommand(new CarrierId("CAR-0001"), "VDT-1", "BK-1", null, null, new AgentRun("finance-matching", "run-1")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_NamingTheAgentAndRunItActsOn_PassesThemToTheCommandForTheTrace()
    {
        AnswerReject(Result<CarrierInvoiceView>.Success((CarrierInvoiceView)null!));

        await CarrierInvoiceEndpoints.RejectLineAsync(
            "CAR-0001", "VDT-1", "BK-1", new RejectCarrierInvoiceLineRequest("Not ours", "finance-matching", "run-1"), _retrier, CancellationToken.None);

        await _mediator.Received(1).Send(
            new RejectCarrierInvoiceLineCommand(new CarrierId("CAR-0001"), "VDT-1", "BK-1", "Not ours", new AgentRun("finance-matching", "run-1")), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("finance-matching", null)]
    [InlineData(null, "run-1")]
    [InlineData("finance-matching", " ")]
    public async Task Decide_AnAgentWithoutItsRun_OrTheOtherWayRound_IsRefusedAsInvalid_AndDecidesNothing(string? agent, string? run)
    {
        var accept = await CarrierInvoiceEndpoints.AcceptLineAsync("CAR-0001", "VDT-1", "BK-1", new AcceptCarrierInvoiceLineRequest(null, null, agent, run), _retrier, CancellationToken.None);
        var reject = await CarrierInvoiceEndpoints.RejectLineAsync("CAR-0001", "VDT-1", "BK-1", new RejectCarrierInvoiceLineRequest("x", agent, run), _retrier, CancellationToken.None);

        accept.Result.Should().BeOfType<ValidationProblem>();
        reject.Result.Should().BeOfType<ValidationProblem>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<AcceptCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<RejectCarrierInvoiceLineCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decide_AnAgentNameOrRunThatIsTooLong_IsRefusedAsInvalid()
    {
        var result = await CarrierInvoiceEndpoints.RejectLineAsync(
            "CAR-0001", "VDT-1", "BK-1", new RejectCarrierInvoiceLineRequest("x", new string('a', 101), "run-1"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<ValidationProblem>();
    }

    [Theory]
    [InlineData("CarrierInvoiceNotFound", 404)]
    [InlineData("CarrierInvoiceLineNotFound", 404)]
    [InlineData("CarrierInvoiceLineAlreadyDecided", 409)]
    [InlineData("CarrierInvoiceLineHasNoOpenCarrierCost", 422)]
    [InlineData("CarrierInvoiceAdjustedAmountNotAboveZero", 400)]
    [InlineData("CarrierInvoiceAdjustedAmountNeedsRemark", 400)]
    public async Task Accept_Refused_AnswersAProblemWithTheStableCode(string code, int status)
    {
        AnswerAccept(Refused(code));

        var result = await CarrierInvoiceEndpoints.AcceptLineAsync("CAR-0001", "VDT-1", "BK-1", null, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions["code"].Should().Be(code);
        problem.ProblemDetails.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task Reject_RunsTheUiCommandWithTheReason()
    {
        AnswerReject(Result<CarrierInvoiceView>.Success((CarrierInvoiceView)null!));

        var result = await CarrierInvoiceEndpoints.RejectLineAsync(
            "CAR-0001", "VDT-1", "BK-1", new RejectCarrierInvoiceLineRequest("Not ours"), _retrier, CancellationToken.None);

        result.Result.Should().BeOfType<Ok<CarrierInvoiceView>>();
        await _mediator.Received(1).Send(
            new RejectCarrierInvoiceLineCommand(new CarrierId("CAR-0001"), "VDT-1", "BK-1", "Not ours"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CarrierInvoiceNotFound", 404)]
    [InlineData("CarrierInvoiceLineNotFound", 404)]
    [InlineData("CarrierInvoiceLineAlreadyDecided", 409)]
    [InlineData("CarrierInvoiceRejectionNeedsReason", 400)]
    public async Task Reject_Refused_AnswersAProblemWithTheStableCode(string code, int status)
    {
        AnswerReject(Refused(code));

        var result = await CarrierInvoiceEndpoints.RejectLineAsync("CAR-0001", "VDT-1", "BK-1", null, _retrier, CancellationToken.None);

        var problem = result.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions["code"].Should().Be(code);
    }
}

public sealed class CarrierInvoiceRoutesTests
{
    [Fact]
    public async Task MapCarrierInvoices_RegistersTheDecisionsUnderTheManageRoleOnly()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddApiVersioning();
        builder.Services.AddSingleton(Substitute.For<IConflictRetrier>());
        await using var app = builder.Build();

        app.MapCarrierInvoices();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();
        foreach (var name in new[] { "AcceptCarrierInvoiceLine", "RejectCarrierInvoiceLine" })
        {
            var endpoint = endpoints.Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().ContainSingle();
            endpoint.RoutePattern.RawText.Should().Contain("/lines/{bookingReference}/");
        }
    }
}
