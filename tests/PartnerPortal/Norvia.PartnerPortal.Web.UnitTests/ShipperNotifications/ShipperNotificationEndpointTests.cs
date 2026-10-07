using System.Text.Json;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.ShipperNotifications.Receive;
using Norvia.PartnerPortal.Domain.ShipperNotifications;
using Norvia.PartnerPortal.Web.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.UnitTests.ShipperNotifications;

public sealed class ShipperNotificationEndpointTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private const string Id = "0f1c0d9e-3b0a-4f43-a6f6-7f1f0d6a9b01";

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IOptions<ShipperEndpointOptions> _options =
        Options.Create(new ShipperEndpointOptions { ApiKeys = { ["SHP-0001"] = "shipper-secret" } });

    private static ShipperNotificationBody Body() => new(
        Id, "BD-2030-0001", "TO-0000042", "LATE", "The truck is stuck in traffic.", "03-10-2030 14:30", null, null, "Eva Planner");

    private Task<Results<Ok<ReceiveShipperNotificationResult>, ProblemHttpResult, UnauthorizedHttpResult>> Receive(
        string shipperId = "SHP-0001", string? apiKey = "shipper-secret", string? correlationId = "corr-1", ShipperNotificationBody? body = null) =>
        ShipperNotificationEndpoint.ReceiveAsync(
            shipperId, apiKey, correlationId, body ?? Body(), new PassThroughRetrier(_mediator), _options, NullLoggerFactory.Instance, CancellationToken.None);

    private void Answers(bool repeated) =>
        _mediator.Send(Arg.Any<ReceiveShipperNotificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveShipperNotificationResult(Guid.Parse(Id), repeated)));

    // The AppHost and the Logic App address the endpoint by this shape, which the AppHost model tests pin from their side.
    [Fact]
    public void PathOf_IsWhereTheLogicAppSendsAShippersNotifications() =>
        ShipperNotificationEndpoint.PathOf("SHP-0001").Should().Be("/shipper/v1/SHP-0001/notifications");

    [Fact]
    public async Task Receive_AValidNotification_IsPutInTheInboxAndAcknowledged()
    {
        Answers(repeated: false);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveShipperNotificationResult>>().Which.Value!.Repeated.Should().BeFalse();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveShipperNotificationCommand>(command =>
                command.PartyId == "SHP-0001"
                && command.NotificationId == Guid.Parse(Id)
                && command.CorrelationId == "corr-1"
                && command.Content.BookingRef == "BD-2030-0001"
                && command.Content.TransportOrderNumber == "TO-0000042"
                && command.Content.ExceptionCode == ShipperExceptionCode.Late
                && command.Content.Message == "The truck is stuck in traffic."
                && command.Content.Timing == DeliveryTiming.WithEta(new DateTime(2030, 10, 3, 14, 30, 0))
                && command.Content.Contact == "Eva Planner"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ARepeatedNotification_IsAcknowledgedToo_FlaggedAsRepeated()
    {
        Answers(repeated: true);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveShipperNotificationResult>>().Which.Value!.Repeated.Should().BeTrue();
    }

    [Fact]
    public async Task Receive_ANotificationWithAnAgreedWindowInsteadOfAnEta_IsTaken()
    {
        Answers(repeated: false);

        var result = await Receive(body: Body() with { Eta = null, DeliverFrom = "04-10-2030 09:00", DeliverUntil = "04-10-2030 12:00" });

        result.Result.Should().BeOfType<Ok<ReceiveShipperNotificationResult>>();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveShipperNotificationCommand>(command =>
                command.Content.Timing == DeliveryTiming.WithWindow(new DateTime(2030, 10, 4, 9, 0, 0), new DateTime(2030, 10, 4, 12, 0, 0))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ANotificationWithoutAnEtaOrAWindow_IsTaken()
    {
        Answers(repeated: false);

        var result = await Receive(body: Body() with { Eta = null });

        result.Result.Should().BeOfType<Ok<ReceiveShipperNotificationResult>>();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveShipperNotificationCommand>(command => command.Content.Timing == DeliveryTiming.None), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task Receive_AMissingOrWrongCredential_IsUnauthorized_AndStoresNothing(string? apiKey)
    {
        var result = await Receive(apiKey: apiKey);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveShipperNotificationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForAShipperWithoutACredential_IsUnauthorized()
    {
        var result = await Receive(shipperId: "SHP-0003");

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    public static TheoryData<string?, ShipperNotificationBody> InvalidRequests()
    {
        var valid = Body();
        return new()
        {
            { null, valid },
            { "corr-1", valid with { NotificationId = null } },
            { "corr-1", valid with { NotificationId = "not-a-uuid" } },
            { "corr-1", valid with { NotificationId = Guid.Empty.ToString() } },
            { "corr-1", valid with { BookingRef = " " } },
            { "corr-1", valid with { TransportOrderNumber = "SH-0000042" } },
            { "corr-1", valid with { TransportOrderNumber = null } },
            { "corr-1", valid with { ExceptionCode = "STOLEN" } },
            { "corr-1", valid with { Message = "" } },
            { "corr-1", valid with { Contact = null } },
            { "corr-1", valid with { Eta = "2030-10-03T14:30:00" } },
            { "corr-1", valid with { Eta = "31-02-2030 10:00" } },
            { "corr-1", valid with { Eta = null, DeliverFrom = "04-10-2030 09:00", DeliverUntil = "later" } },
            { "corr-1", valid with { Eta = null, DeliverFrom = "04-10-2030 09:00" } },
            { "corr-1", valid with { DeliverFrom = "04-10-2030 09:00", DeliverUntil = "04-10-2030 12:00" } },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Receive_AnInvalidNotification_IsABadRequest_AndStoresNothing(string? correlationId, ShipperNotificationBody body)
    {
        var result = await Receive(correlationId: correlationId, body: body);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveShipperNotificationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForAShipperTheDirectoryDoesNotKnow_IsNotFound()
    {
        _mediator.Send(Arg.Any<ReceiveShipperNotificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReceiveShipperNotificationResult>.NotFound());

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    public static TheoryData<string> Samples()
    {
        var names = typeof(ShipperNotificationEndpointTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("shipper-notifications/", StringComparison.Ordinal));
        var data = new TheoryData<string>();
        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }

    // What the Shipper notification map produces is what the endpoint takes: the field names of the format bind to the body.
    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Receive_ASampleOfTheShipperNotificationFormat_IsTaken(string sample)
    {
        Answers(repeated: false);
        using var stream = typeof(ShipperNotificationEndpointTests).Assembly.GetManifestResourceStream(sample)!;
        var body = await JsonSerializer.DeserializeAsync<ShipperNotificationBody>(stream, WebJson, Xunit.TestContext.Current.CancellationToken);

        var result = await Receive(body: body);

        result.Result.Should().BeOfType<Ok<ReceiveShipperNotificationResult>>();
    }
}
