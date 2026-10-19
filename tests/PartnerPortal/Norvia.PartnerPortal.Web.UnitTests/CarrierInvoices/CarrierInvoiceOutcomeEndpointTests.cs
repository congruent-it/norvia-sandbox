using System.Text.Json;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application.CarrierInvoices.Outcome;
using Norvia.PartnerPortal.Domain.CarrierInvoices;
using Norvia.PartnerPortal.Web.CarrierBookings;
using Norvia.PartnerPortal.Web.CarrierInvoices;

namespace Norvia.PartnerPortal.Web.UnitTests.CarrierInvoices;

public sealed class CarrierInvoiceOutcomeEndpointTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IOptions<CarrierEndpointOptions> _options =
        Options.Create(new CarrierEndpointOptions { ApiKeys = { ["CAR-0001"] = "carrier-secret" } });

    private static CarrierInvoiceOutcomeBody Outcome() => new(
        "VDT-2026-0412", 190.50m,
        [
            new CarrierInvoiceOutcomeLineBody("TRX-998", "MATCHED", 70.50m, null),
            new CarrierInvoiceOutcomeLineBody("TRX-999", "ACCEPTED_ADJUSTED", 120m, " Waiting time "),
            new CarrierInvoiceOutcomeLineBody("TRX-1000", "REJECTED", 0m, null),
        ]);

    private Task<Results<Ok<ReceiveCarrierInvoiceOutcomeResult>, ProblemHttpResult, UnauthorizedHttpResult>> Receive(
        string carrierId = "CAR-0001", string? apiKey = "carrier-secret", string? correlationId = "corr-1", CarrierInvoiceOutcomeBody? body = null) =>
        CarrierInvoiceOutcomeEndpoint.ReceiveAsync(
            carrierId, apiKey, correlationId, body ?? Outcome(), new PassThroughRetrier(_mediator), _options, NullLoggerFactory.Instance, CancellationToken.None);

    private void Answers(bool repeated) =>
        _mediator.Send(Arg.Any<ReceiveCarrierInvoiceOutcomeCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new ReceiveCarrierInvoiceOutcomeResult("VDT-2026-0412", repeated)));

    // The Logic App addresses the endpoint by this shape.
    [Fact]
    public void PathOf_IsWhereTheLogicAppTellsACarriersInvoiceOutcomes() =>
        CarrierInvoiceOutcomeEndpoint.PathOf("CAR-0001").Should().Be("/carrier/v1/CAR-0001/invoice-outcomes");

    [Fact]
    public async Task Receive_AValidOutcome_IsStoredAndAcknowledged()
    {
        Answers(repeated: false);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveCarrierInvoiceOutcomeResult>>().Which.Value!.Repeated.Should().BeFalse();
        await _mediator.Received(1).Send(
            Arg.Is<ReceiveCarrierInvoiceOutcomeCommand>(command =>
                command.PartyId == "CAR-0001"
                && command.CorrelationId == "corr-1"
                && command.InvoiceNumber == "VDT-2026-0412"
                && command.ApprovedAmount == 190.50m
                && command.Lines.Count == 3
                && command.Lines[0] == new CarrierInvoiceOutcomeLine("TRX-998", CarrierInvoiceOutcomeResult.Matched, 70.50m, null)
                && command.Lines[1] == new CarrierInvoiceOutcomeLine("TRX-999", CarrierInvoiceOutcomeResult.AcceptedAdjusted, 120m, "Waiting time")
                && command.Lines[2].Result == CarrierInvoiceOutcomeResult.Rejected),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_AnOutcomeAlreadyStored_IsAcknowledgedToo_FlaggedAsRepeated()
    {
        Answers(repeated: true);

        var result = await Receive();

        result.Result.Should().BeOfType<Ok<ReceiveCarrierInvoiceOutcomeResult>>().Which.Value!.Repeated.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task Receive_AMissingOrWrongCredential_IsUnauthorized_AndStoresNothing(string? apiKey)
    {
        var result = await Receive(apiKey: apiKey);

        result.Result.Should().BeOfType<UnauthorizedHttpResult>();
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierInvoiceOutcomeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForACarrierWithoutACredential_IsUnauthorized() =>
        (await Receive(carrierId: "CAR-0003")).Result.Should().BeOfType<UnauthorizedHttpResult>();

    public static TheoryData<string?, CarrierInvoiceOutcomeBody> InvalidRequests()
    {
        var valid = Outcome();
        return new()
        {
            { null, valid },
            { " ", valid },
            { "corr-1", valid with { InvoiceNo = null } },
            { "corr-1", valid with { InvoiceNo = " " } },
            { "corr-1", valid with { ApprovedAmount = null } },
            { "corr-1", valid with { ApprovedAmount = -1m } },
            { "corr-1", valid with { ApprovedAmount = 10.005m } },
            { "corr-1", valid with { Lines = null } },
            { "corr-1", valid with { Lines = [] } },
            { "corr-1", valid with { Lines = [null] } },
            { "corr-1", valid with { Lines = [valid.Lines![0]! with { BookingRef = null }] } },
            { "corr-1", valid with { Lines = [valid.Lines![0]! with { Result = null }] } },
            { "corr-1", valid with { Lines = [valid.Lines![0]! with { Result = "PRICE_MISMATCH" }] } },
            { "corr-1", valid with { Lines = [valid.Lines![0]! with { AcceptedAmount = null }] } },
            { "corr-1", valid with { Lines = [valid.Lines![0]! with { AcceptedAmount = -0.01m }] } },
            { "corr-1", valid with { Lines = [valid.Lines![0]! with { AcceptedAmount = 1.234m }] } },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Receive_AnOutcomeBreakingTheFormat_IsABadRequest_AndStoresNothing(string? correlationId, CarrierInvoiceOutcomeBody body)
    {
        var result = await Receive(correlationId: correlationId, body: body);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await _mediator.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReceiveCarrierInvoiceOutcomeCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Receive_ForNoCarrierPersona_IsNotFound()
    {
        _mediator.Send(Arg.Any<ReceiveCarrierInvoiceOutcomeCommand>(), Arg.Any<CancellationToken>()).Returns(Result<ReceiveCarrierInvoiceOutcomeResult>.NotFound());

        var result = await Receive();

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    public static TheoryData<string> Samples()
    {
        var names = typeof(CarrierInvoiceOutcomeEndpointTests).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("carrier-invoice-outcomes/", StringComparison.Ordinal));
        var data = new TheoryData<string>();
        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }

    // The sample outcomes of the format are what the endpoint takes: the field names of the format bind to the body.
    [Theory]
    [MemberData(nameof(Samples))]
    public async Task Receive_ASampleOfTheOutcomeFormat_IsTaken(string sample)
    {
        Answers(repeated: false);
        using var stream = typeof(CarrierInvoiceOutcomeEndpointTests).Assembly.GetManifestResourceStream(sample)!;
        var body = await JsonSerializer.DeserializeAsync<CarrierInvoiceOutcomeBody>(stream, WebJson, Xunit.TestContext.Current.CancellationToken);

        var result = await Receive(body: body);

        result.Result.Should().BeOfType<Ok<ReceiveCarrierInvoiceOutcomeResult>>();
    }
}
