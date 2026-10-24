using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierBookings.Receive;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Web.CarrierBookings;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierBookings;

public sealed class CarrierBookingEndpointTests
{
    private static readonly System.Text.Json.JsonSerializerOptions WebJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IPartnerPortalFaults _faults = Substitute.For<IPartnerPortalFaults>();
    private readonly IOptions<CarrierEndpointOptions> _options =
        Options.Create(new CarrierEndpointOptions { ApiKeys = { ["CAR-0001"] = "carrier-secret" } });

    private static CarrierBookingRequestBody Body(string? number = "CB-0000001", string? shipment = "SH-0000042") => new(
        number, shipment,
        "Waalhaven Oostzijde 88", "3087 BM", "Rotterdam", "NLD", new DateTime(2030, 10, 2, 7, 30, 0), new DateTime(2030, 10, 2, 11, 0, 0),
        "Kölner Landstraße 301", "40591", "Düsseldorf", "DEU", null, null,
        "Getränke Schmitz GmbH",
        [new CarrierBookingGoodsLineBody("pallet", 4, 2400m, 120m, 80m, 160m)]);

    private Task<Results<Ok<ReceiveCarrierBookingResult>, ProblemHttpResult, UnauthorizedHttpResult>> Receive(
        string carrierId = "CAR-0001", string? apiKey = "carrier-secret", string? correlationId = "release-1", CarrierBookingRequestBody? body = null) =>
        CarrierBookingEndpoint.ReceiveAsync(
            carrierId, apiKey, correlationId, body ?? Body(), new PassThroughRetrier(_mediator), _options, _faults, NullLoggerFactory.Instance, CancellationToken.None);

    [Fact]
    public async Task Receive_ARequestWithTheCredential_IsPutInTheInboxAndAnswered()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierBookingResult("CB-0000001", Repeated: false)));

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveCarrierBookingResult>>().Which.Value!.Repeated.Should().BeFalse();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveCarrierBookingCommand>(command =>
                command.PartyId == "CAR-0001"
                && command.CarrierBookingNumber == "CB-0000001"
                && command.ShipmentNumber == "SH-0000042"
                && command.CorrelationId == "release-1"
                && command.Details.Collection.City == "Rotterdam"
                && command.Details.Collection.Country == "NLD"
                && command.Details.Collection.From == new DateTime(2030, 10, 2, 7, 30, 0)
                && command.Details.Delivery.City == "Düsseldorf"
                && command.Details.ConsigneeName == "Getränke Schmitz GmbH"
                && command.Details.Goods.Count == 1
                && command.Details.Goods[0].PackageType == "pallet"
                && command.Details.Goods[0].HeightCm == 160m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_TheCarrierCost_IsPassedOnWithItsChargeLinesAndWhetherItIsSpot()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierBookingResult("CB-0000001", Repeated: false)));
        var cost = new CarrierCostBody("EUR", 412.50m, [new CarrierChargeLineBody("Freight", 400m), new CarrierChargeLineBody("MinimumChargeAdjustment", 12.50m)], IsSpot: true);

        await Receive(body: Body() with { CarrierCost = cost });

        await _mediator.Received(1).Send(
            Arg.Is<ReceiveCarrierBookingCommand>(command =>
                command.Details.CarrierCost != null
                && command.Details.CarrierCost.Currency == "EUR"
                && command.Details.CarrierCost.Total == 412.50m
                && command.Details.CarrierCost.IsSpot
                && command.Details.CarrierCost.ChargeLines.Count == 2
                && command.Details.CarrierCost.ChargeLines[1].Type == "MinimumChargeAdjustment"
                && command.Details.CarrierCost.ChargeLines[1].Amount == 12.50m),
            Arg.Any<CancellationToken>());
    }

    public static TheoryData<CarrierCostBody> IncompleteCosts => new()
    {
        new CarrierCostBody(null, 10m, [new CarrierChargeLineBody("Freight", 10m)], false),
        new CarrierCostBody("USD", 10m, [new CarrierChargeLineBody("Freight", 10m)], false),
        new CarrierCostBody("EUR", null, [new CarrierChargeLineBody("Freight", 10m)], false),
        new CarrierCostBody("EUR", 10m, [new CarrierChargeLineBody("Freight", 10m)], null),
        new CarrierCostBody("EUR", 10m, null, false),
        new CarrierCostBody("EUR", 10m, [], false),
        new CarrierCostBody("EUR", 10m, [new CarrierChargeLineBody(null, 10m)], false),
        new CarrierCostBody("EUR", 10m, [new CarrierChargeLineBody("FreightCharge", 10m)], false),
        new CarrierCostBody("EUR", 10m, [new CarrierChargeLineBody("Freight", null)], false),
        new CarrierCostBody("EUR", 11m, [new CarrierChargeLineBody("Freight", 10m)], false),
    };

    [Theory]
    [MemberData(nameof(IncompleteCosts))]
    public async Task Receive_AnIncompleteCarrierCost_IsABadRequest_SoNoOneConfirmsWithoutSeeingAPrice(CarrierCostBody cost)
    {
        var result = await Receive(body: Body() with { CarrierCost = cost });

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_AShippersFreightChargeInTheBody_NeverReachesTheInbox()
    {
        ReceiveCarrierBookingCommand? received = null;
        _mediator.Send(Arg.Do<ReceiveCarrierBookingCommand>(command => received = command), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierBookingResult("CB-0000001", Repeated: false)));
        const string json = """
            {"carrierBookingNumber":"CB-0000001","shipmentNumber":"SH-0000042",
             "carrierCost":{"currency":"EUR","total":10,"chargeLines":[{"type":"Freight","amount":10}],"isSpot":false},
             "freightCharge":{"total":987.65}}
            """;
        var body = System.Text.Json.JsonSerializer.Deserialize<CarrierBookingRequestBody>(json, WebJson)!;

        await Receive(body: body);

        var stored = System.Text.Json.JsonSerializer.Serialize(received!.Details);
        stored.Should().NotContain("987").And.NotContainEquivalentOf("freightCharge");
    }

    [Fact]
    public async Task Receive_ARequestWithoutACarrierCost_IsStillTaken()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierBookingResult("CB-0000001", Repeated: false)));

        await Receive();

        await _mediator.Received(1).Send(Arg.Is<ReceiveCarrierBookingCommand>(command => command.Details.CarrierCost == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ARepeatedRequest_IsAnsweredAsSuccess_FlaggedAsRepeated()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierBookingResult("CB-0000001", Repeated: true)));

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveCarrierBookingResult>>().Which.Value!.Repeated.Should().BeTrue();
    }

    [Fact]
    public async Task Receive_ARequestWithoutGoodsOrWindows_IsStillTaken()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierBookingResult("CB-0000001", Repeated: false)));

        var result = await Receive(body: new CarrierBookingRequestBody("CB-0000001", "SH-0000042", null, null, null, null, null, null, null, null, null, null, null, null, null, null));

        result.Result.Should().BeOfType<Ok<ReceiveCarrierBookingResult>>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task Receive_AMissingOrWrongCredential_IsUnauthorized_AndPutsNothingInTheInbox(string? apiKey)
    {
        var result = await Receive(apiKey: apiKey);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForACarrierWithoutACredential_IsUnauthorized()
    {
        var result = await Receive(carrierId: "CAR-0002");

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
    }

    [Theory]
    [InlineData(null, "CB-1", "SH-1")]
    [InlineData("release-1", null, "SH-1")]
    [InlineData("release-1", "CB-1", " ")]
    public async Task Receive_AnIncompleteRequest_IsABadRequest(string? correlationId, string? number, string? shipment)
    {
        var result = await Receive(correlationId: correlationId, body: Body(number, shipment));

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForACarrierTheDirectoryDoesNotKnow_IsNotFound()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierBookingCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReceiveCarrierBookingResult>.NotFound());

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Receive_TheEndpointDownFaultOn_Answers503_AndKeepsNothing()
    {
        _faults.BookingEndpointDown.Returns(true);

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Receive_TheEndpointDownFaultOn_Answers503_EvenToAWrongCredential()
    {
        _faults.BookingEndpointDown.Returns(true);

        var result = await Receive(apiKey: "wrong");

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public async Task Receive_TheEndpointDownFaultOn_RecordsTheHopFailed_WithTheFaultThatCausedIt()
    {
        var loggers = new FakeLoggerProvider();
        using var factory = new LoggerFactory([loggers]);
        _faults.BookingEndpointDown.Returns(true);

        await CarrierBookingEndpoint.ReceiveAsync(
            "CAR-0001", "carrier-secret", "release-1", Body(), new PassThroughRetrier(_mediator), _options, _faults, factory, CancellationToken.None);

        var scope = loggers.Collector.LatestRecord.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();
        scope.Should().Contain(new Dictionary<string, object?>
        {
            ["flow"] = "carrier-booking",
            ["outcome"] = "Failed",
            ["failureCode"] = "CarrierEndpointUnavailable",
            ["fault"] = "carrier-booking-endpoint-down",
        });
    }
}
