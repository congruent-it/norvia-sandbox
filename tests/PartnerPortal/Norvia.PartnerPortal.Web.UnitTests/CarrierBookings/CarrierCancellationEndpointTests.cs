using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierBookings.Cancel;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class CarrierCancellationEndpointTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IOptions<CarrierEndpointOptions> _options =
        Options.Create(new CarrierEndpointOptions { ApiKeys = { ["CAR-0001"] = "carrier-secret" } });

    private static CarrierCancellationBody Cancelled(string? reason = "Truck broke down") =>
        new("CB-0000001", "VDT-77812", "SH-0000042", "CANCELLED", reason);

    private static CarrierCancellationBody Withdrawn() => new("CB-0000001", null, "SH-0000042", "WITHDRAWN", null);

    private Task<Results<Ok<ReceiveCarrierCancellationResult>, ProblemHttpResult, UnauthorizedHttpResult>> Receive(
        CarrierCancellationBody? body = null, string carrierId = "CAR-0001", string? apiKey = "carrier-secret", string? correlationId = "cancel-1") =>
        CarrierCancellationEndpoint.ReceiveAsync(
            carrierId, apiKey, correlationId, body ?? Cancelled(), _mediator, _options, NullLoggerFactory.Instance, CancellationToken.None);

    private void MediatorAnswers(bool applied, bool repeated) =>
        _mediator.Send(Arg.Any<ReceiveCarrierCancellationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierCancellationResult("CB-0000001", applied, repeated)));

    [Fact]
    public void PathOf_NamesTheCarriersCancellationEndpoint() =>
        CarrierCancellationEndpoint.PathOf("CAR-0001").Should().Be("/carrier/v1/CAR-0001/cancellations");

    [Fact]
    public async Task Receive_ACancellationWithTheCredential_IsAppliedAndAcknowledged()
    {
        MediatorAnswers(applied: true, repeated: false);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveCarrierCancellationResult>>().Which.Value!.Repeated.Should().BeFalse();
        await _mediator.Received(1).Send(
            new ReceiveCarrierCancellationCommand("CAR-0001", "CB-0000001", CarrierCancellationKind.Cancelled, "Truck broke down", "cancel-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_AWithdrawal_IsAppliedAsAWithdrawal_WithoutAReason()
    {
        MediatorAnswers(applied: true, repeated: false);

        var result = await Receive(Withdrawn());

        result.Result.Should().BeOfType<Ok<ReceiveCarrierCancellationResult>>();
        await _mediator.Received(1).Send(
            new ReceiveCarrierCancellationCommand("CAR-0001", "CB-0000001", CarrierCancellationKind.Withdrawn, null, "cancel-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ARepeat_IsAcknowledgedAsSuccess_FlaggedAsRepeated()
    {
        MediatorAnswers(applied: false, repeated: true);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveCarrierCancellationResult>>().Which.Value!.Repeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task Receive_AMissingOrWrongCredential_IsUnauthorized_AndChangesNothing(string? apiKey)
    {
        var result = await Receive(apiKey: apiKey);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierCancellationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForACarrierWithoutACredential_IsUnauthorized()
    {
        var result = await Receive(carrierId: "CAR-0002");

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    public static TheoryData<string?, CarrierCancellationBody> Incomplete => new()
    {
        { null, Cancelled() },
        { "cancel-1", Cancelled() with { CarrierBookingNumber = null } },
        { "cancel-1", Cancelled() with { ShipmentNumber = " " } },
        { "cancel-1", Cancelled() with { CarrierBookingNumber = "CB-1" } },
        { "cancel-1", Cancelled() with { ShipmentNumber = "SH-42" } },
        { "cancel-1", Cancelled() with { Kind = null } },
        { "cancel-1", Cancelled() with { Kind = "REVOKED" } },
        { "cancel-1", Cancelled(reason: null) },
        { "cancel-1", Cancelled() with { CarrierBookingReference = null } },
        { "cancel-1", Cancelled(reason: new string('x', 501)) },
        { "cancel-1", Withdrawn() with { ReasonText = "Why" } },
        { "cancel-1", Withdrawn() with { CarrierBookingReference = "VDT-1" } },
    };

    [Theory]
    [MemberData(nameof(Incomplete))]
    public async Task Receive_AMessageThatBreaksTheFormat_IsABadRequest(string? correlationId, CarrierCancellationBody body)
    {
        var result = await Receive(body, correlationId: correlationId);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierCancellationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_WhenTheApplicationFindsItInvalid_IsABadRequest_NotAnUnknownCarrier()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierCancellationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReceiveCarrierCancellationResult>.Invalid(new ValidationError("ReasonText", "A cancellation needs a reason.")));

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Receive_ForACarrierTheDirectoryDoesNotKnow_IsNotFound()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierCancellationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReceiveCarrierCancellationResult>.NotFound());

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }
}
