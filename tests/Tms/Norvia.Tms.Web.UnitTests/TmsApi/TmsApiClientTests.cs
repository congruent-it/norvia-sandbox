using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Testing;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.TransportOrders;
using Norvia.Tms.Web.Shipments;
using Norvia.Tms.Web.TmsApi;
using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TmsApi;

public sealed class TmsApiClientTests : IDisposable
{
    private readonly StubHandler _tmsApi = new();
    private readonly ITmsApiTokens _tokens = Substitute.For<ITmsApiTokens>();
    private readonly FakeLogger<TmsApiClient> _logger = new();
    private readonly TmsUiActivities _activities = new("Norvia.Tms.Web.Tests");
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;
    private readonly HttpClient _http;
    private readonly TmsApiClient _client;

    public TmsApiClientTests()
    {
        _http = new HttpClient(_tmsApi) { BaseAddress = new Uri("http://tms-api.example") };
        _tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tms-ui-token");
        _client = new TmsApiClient(_http, _tokens, _activities, _logger);
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source == _activities.Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Add,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _activities.Dispose();
        _http.Dispose();
        _tmsApi.Dispose();
    }

    [Fact]
    public async Task ReRateAsync_PostsToTheOrdersReRate_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        await ReRateAsync("TO-0000043");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/transport-orders/TO-0000043/re-rate"));
        _tmsApi.Request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        _tmsApi.Request.Headers.Authorization.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReRateAsync_TwoReRates_GoUnderKeysOfTheirOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("RatingFailed", failure: ("NoMatchingLane", "No Lane.")));
        await ReRateAsync("TO-0000043");
        var first = _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single();

        await ReRateAsync("TO-0000043");

        _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single().Should().NotBe(first);
    }

    [Fact]
    public async Task ReRateAsync_AnsweredRated_IsRated()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.Rated());
    }

    [Fact]
    public async Task ReRateAsync_AnsweredRatingFailed_FailedAgainWithTheNewFailure()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("RatingFailed", failure: ("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.FailedAgain(new RatingFailureView("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));
    }

    [Fact]
    public async Task ReRateAsync_Conflict_IsRefusedWithTheTmsReason()
    {
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409,
              "title": "The Transport Order can't be re-rated.",
              "detail": "Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated."
            }
            """,
            "application/problem+json");

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.Refused("Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated."));
    }

    [Fact]
    public async Task ReRateAsync_NotFound_IsRefusedAsNoSuchOrder()
    {
        _tmsApi.Answer(HttpStatusCode.NotFound, """{ "status": 404, "title": "Transport Order TO-0000043 not found." }""", "application/problem+json");

        var answer = await ReRateAsync("TO-0000043");

        answer.Should().Be(new ReRateAnswer.Refused("There is no Transport Order TO-0000043."));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReRateAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var reRate = () => ReRateAsync("TO-0000043");

        await reRate.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AssignAsync_ToACarrier_PostsTheCarrierToTheOrdersAssign_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", "CAR-0002");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/transport-orders/TO-0000042/assign"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        JsonNode.Parse(_tmsApi.RequestBody!)!.ToJsonString().Should().Be("""{"assignedTo":"Carrier","carrierId":"CAR-0002"}""");
    }

    [Fact]
    public async Task AssignAsync_ToOwnFleet_PostsOwnFleetWithoutACarrier()
    {
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", carrierId: null);

        JsonNode.Parse(_tmsApi.RequestBody!)!.ToJsonString().Should().Be("""{"assignedTo":"OwnFleet"}""");
    }

    [Fact]
    public async Task AssignAsync_TwoAssignments_GoUnderKeysOfTheirOwn()
    {
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));
        await AssignAsync("TO-0000042", "CAR-0002");
        var first = _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single();

        await AssignAsync("TO-0000043", "CAR-0002");

        _tmsApi.Request!.Headers.GetValues("Idempotency-Key").Single().Should().NotBe(first);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.OK)] // A retry the TMS answers as a repeat.
    public async Task AssignAsync_Assigned_IsPlannedWithItsShipmentNumber(HttpStatusCode status)
    {
        _tmsApi.Answer(status, Assigned("SH-0000012"));

        var answer = await AssignAsync("TO-0000042", "CAR-0002");

        answer.Should().Be(new AssignAnswer.Planned("SH-0000012"));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "TransportOrderNotFound", "There is no Transport Order TO-0000042.")]
    [InlineData(HttpStatusCode.Conflict, "TransportOrderAlreadyPlanned", "Transport Order TO-0000042 is Planned already.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "InactiveCarrier", "Carrier CAR-0004 is not active.")]
    public async Task AssignAsync_Refused_IsRefusedWithTheTmsReason(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The Transport Order can't be assigned.", "detail": "{{reason}}", "code": "{{code}}" }""",
            "application/problem+json");

        var answer = await AssignAsync("TO-0000042", "CAR-0004");

        answer.Should().Be(new AssignAnswer.Refused(reason));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AssignAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var assign = () => AssignAsync("TO-0000042", "CAR-0002");

        await assign.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AssignAsync_Assigned_RecordsItsOutcomeOnTheAssignmentsTrail()
    {
        // The TMS starts the assignment's Message trail; the Planner's click joins it once the answer names it.
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", "CAR-0002");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-planning",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000042",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task AssignAsync_Refused_RecordsItRefused_WithTheTmsCode_OnTheRefusalsTrail()
    {
        // The TMS traces a refused assignment on a trail of its own, which its problem names.
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409, "title": "The Transport Order can't be assigned.", "detail": "Transport Order TO-0000042 is Planned already.",
              "code": "TransportOrderAlreadyPlanned", "correlationId": "corr-refused"
            }
            """,
            "application/problem+json");

        await AssignAsync("TO-0000042", "CAR-0002");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-refused",
            ["flow"] = "transport-planning",
            ["transportOrderNumber"] = "TO-0000042",
            ["outcome"] = "Refused",
            ["failureCode"] = "TransportOrderAlreadyPlanned",
        });
    }

    [Fact]
    public async Task AssignAsync_SendsTheAssignmentInASpanOfItsOwn_WhichTheAnswersLogIsWrittenIn()
    {
        // The Integration console names the TMS UI's hop after it, not after Blazor's span for the click.
        _tmsApi.Answer(HttpStatusCode.Created, Assigned("SH-0000012"));

        await AssignAsync("TO-0000042", "CAR-0002");

        var span = _spans.Should().ContainSingle().Subject;
        span.DisplayName.Should().Be("Assign Transport Order");
        _logger.LatestRecord.Timestamp.UtcDateTime.Should().BeOnOrAfter(span.StartTimeUtc).And.BeOnOrBefore(span.StartTimeUtc + span.Duration);
    }

    [Fact]
    public async Task ReassignAsync_ToACarrier_PostsTheCarrierToTheShipmentsReassign_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Reassigned());

        await ReassignAsync("SH-0000012", "CAR-0001");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/shipments/SH-0000012/reassign"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        JsonNode.Parse(_tmsApi.RequestBody!)!.ToJsonString().Should().Be("""{"assignedTo":"Carrier","carrierId":"CAR-0001"}""");
    }

    [Fact]
    public async Task ReassignAsync_ToOwnFleet_PostsOwnFleetWithoutACarrier()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Reassigned(ownFleet: true));

        await ReassignAsync("SH-0000012", carrierId: null);

        JsonNode.Parse(_tmsApi.RequestBody!)!.ToJsonString().Should().Be("""{"assignedTo":"OwnFleet"}""");
    }

    [Fact]
    public async Task ReassignAsync_Reassigned_IsReassignedWithWhoCarriesItNow()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Reassigned());

        var answer = await ReassignAsync("SH-0000012", "CAR-0001");

        answer.Should().Be(new ReassignAnswer.Reassigned(new CarriedByView(AssignedTo.Carrier, new CarrierView("CAR-0001", "Van Dijk Transport"))));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ShipmentNotFound", "There is no Shipment SH-0000012.")]
    [InlineData(HttpStatusCode.Conflict, "ShipmentNotPlanned", "Shipment SH-0000012 isn't Planned.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "InactiveCarrier", "Carrier CAR-0004 is not active.")]
    public async Task ReassignAsync_Refused_IsRefusedWithTheTmsReason(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The Shipment can't be reassigned.", "detail": "{{reason}}", "code": "{{code}}" }""",
            "application/problem+json");

        var answer = await ReassignAsync("SH-0000012", "CAR-0004");

        answer.Should().Be(new ReassignAnswer.Refused(reason));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReassignAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var reassign = () => ReassignAsync("SH-0000012", "CAR-0001");

        await reassign.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ReassignAsync_Reassigned_RecordsItsOutcomeOnTheAssignmentsTrail()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Reassigned());

        await ReassignAsync("SH-0000012", "CAR-0001");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-reassignment",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000042",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task ReassignAsync_Refused_RecordsItRefused_WithoutATransportOrderNumber()
    {
        // The problem doesn't name the Transport Order, so the trail carries only what the reassignment itself knows.
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409, "title": "The Shipment can't be reassigned.", "detail": "Shipment SH-0000012 isn't Planned.",
              "code": "ShipmentNotPlanned", "correlationId": "corr-refused"
            }
            """,
            "application/problem+json");

        await ReassignAsync("SH-0000012", "CAR-0001");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-refused",
            ["flow"] = "transport-planning",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Refused",
            ["failureCode"] = "ShipmentNotPlanned",
        }).And.NotContainKey("transportOrderNumber");
    }

    [Fact]
    public async Task ReassignAsync_SendsTheReassignmentInASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Reassigned());

        await ReassignAsync("SH-0000012", "CAR-0001");

        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Reassign Shipment");
    }

    [Fact]
    public async Task ReleaseAsync_PostsToTheShipmentsRelease_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Released());

        await ReleaseAsync("SH-0000012");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/shipments/SH-0000012/release"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReleaseAsync_Released_IsReleased()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Released());

        var answer = await ReleaseAsync("SH-0000012");

        answer.Should().Be(new ReleaseAnswer.Released());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ShipmentNotFound", "There is no Shipment SH-0000012.")]
    [InlineData(HttpStatusCode.Conflict, "ShipmentNotPlanned", "Shipment SH-0000012 isn't Planned.")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "NoMatchingLane", "Van Dijk Transport's Carrier Rate Card has no Lane for it.")]
    public async Task ReleaseAsync_Refused_IsRefusedWithTheTmsReasonAndCode(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The Shipment can't be released.", "detail": "{{reason}}", "code": "{{code}}" }""",
            "application/problem+json");

        var answer = await ReleaseAsync("SH-0000012");

        answer.Should().Be(new ReleaseAnswer.Refused(reason, code));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReleaseAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var release = () => ReleaseAsync("SH-0000012");

        await release.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ReleaseAsync_Released_RecordsItsOutcomeOnTheAssignmentsTrail()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Released());

        await ReleaseAsync("SH-0000012");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-release",
            ["flow"] = "transport-planning",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000042",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task ReleaseAsync_SendsTheReleaseInASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Released());

        await ReleaseAsync("SH-0000012");

        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Release Shipment");
    }

    [Fact]
    public async Task ReleaseWithSpotPriceAsync_PostsTheAmountAndRemark_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Released());

        var answer = await ReleaseWithSpotPriceAsync("SH-0000012", 480m, "agreed with Van Dijk by phone, €480");

        answer.Should().Be(new ReleaseAnswer.Released());
        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/shipments/SH-0000012/release-with-spot-price"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        JsonNode.DeepEquals(JsonNode.Parse(_tmsApi.RequestBody!), JsonNode.Parse("""{ "amount": 480, "remark": "agreed with Van Dijk by phone, €480" }""")).Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ShipmentNotFound", "There is no Shipment SH-0000012.")]
    [InlineData(HttpStatusCode.Conflict, "CarrierRateCardPricesShipment", "Van Dijk Transport's Carrier Rate Card prices Shipment SH-0000012.")]
    public async Task ReleaseWithSpotPriceAsync_Refused_IsRefusedWithTheTmsReasonAndCode(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The Shipment can't be released with a Spot price.", "detail": "{{reason}}", "code": "{{code}}" }""",
            "application/problem+json");

        var answer = await ReleaseWithSpotPriceAsync("SH-0000012", 480m, "by phone");

        answer.Should().Be(new ReleaseAnswer.Refused(reason, code));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReleaseWithSpotPriceAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var release = () => ReleaseWithSpotPriceAsync("SH-0000012", 480m, "by phone");

        await release.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ReleaseWithSpotPriceAsync_Released_RecordsItsOutcomeOnTheAssignmentsTrailInASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Released());

        await ReleaseWithSpotPriceAsync("SH-0000012", 480m, "by phone");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-release",
            ["flow"] = "transport-planning",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        });
        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Release Shipment with Spot price");
    }

    [Fact]
    public async Task ReleaseWithoutProofOfDeliveryAsync_PostsTheRemarkAndWhoReleases_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, ReleasedWithoutProof("Billable"));

        await ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/transport-orders/TO-0000043/release-without-proof-of-delivery"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
        JsonNode.DeepEquals(JsonNode.Parse(_tmsApi.RequestBody!), JsonNode.Parse("""{ "remark": "CMR by post", "releasedBy": "Sanne Bakker" }""")).Should().BeTrue();
    }

    [Theory]
    [InlineData("Billable", BillingState.Billable)]
    [InlineData("NotYetBillable", BillingState.NotYetBillable)]
    public async Task ReleaseWithoutProofOfDeliveryAsync_Released_SaysWhereTheOrderStandsForBilling(string state, BillingState expected)
    {
        _tmsApi.Answer(HttpStatusCode.OK, ReleasedWithoutProof(state));

        var answer = await ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post");

        answer.Should().Be(new ReleaseWithoutProofOfDeliveryAnswer.Released(expected));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "TransportOrderNotFound", "There is no Transport Order TO-0000043.")]
    [InlineData(HttpStatusCode.Conflict, "AlreadyReleased", "Transport Order TO-0000043 was released without a Proof of delivery already.")]
    public async Task ReleaseWithoutProofOfDeliveryAsync_Refused_IsRefusedWithTheTmsReason_OnTheTrailTheRefusalNames(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "detail": "{{reason}}", "code": "{{code}}", "correlationId": "corr-refused" }""",
            "application/problem+json");

        var answer = await ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post");

        answer.Should().Be(new ReleaseWithoutProofOfDeliveryAnswer.Refused(reason));
        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-refused",
            ["flow"] = "billing",
            ["transportOrderNumber"] = "TO-0000043",
            ["failureCode"] = code,
            ["outcome"] = "Refused",
        });
    }

    [Fact]
    public async Task ReleaseWithoutProofOfDeliveryAsync_Released_RecordsItsOutcomeOnTheBillingTrail_InASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, ReleasedWithoutProof("Billable"));

        await ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-release",
            ["flow"] = "billing",
            ["transportOrderNumber"] = "TO-0000043",
            ["shipmentNumber"] = "SH-0000012",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Release without Proof of delivery");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReleaseWithoutProofOfDeliveryAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var release = () => ReleaseWithoutProofOfDeliveryAsync("TO-0000043", "CMR by post");

        await release.Should().ThrowAsync<HttpRequestException>();
    }

    private Task<ReleaseWithoutProofOfDeliveryAnswer> ReleaseWithoutProofOfDeliveryAsync(string number, string remark) =>
        _client.ReleaseWithoutProofOfDeliveryAsync(number, remark, Xunit.TestContext.Current.CancellationToken);

    // As the TMS API answers a release without a Proof of delivery: ReleaseWithoutProofOfDeliveryResult, with its enums as strings.
    private static string ReleasedWithoutProof(string billingState) =>
        $$"""{ "transportOrderNumber": "TO-0000043", "shipmentNumber": "SH-0000012", "billingState": "{{billingState}}", "correlationId": "corr-release" }""";

    [Fact]
    public async Task WithdrawAsync_PostsToTheCarrierBookingsWithdraw_AsTheTmsUi_UnderAnIdempotencyKey()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Withdrawn());

        await WithdrawAsync("CB-0000007");

        _tmsApi.Request!.Method.Should().Be(HttpMethod.Post);
        _tmsApi.Request.RequestUri.Should().Be(new Uri("http://tms-api.example/v1/carrier-bookings/CB-0000007/withdraw"));
        _tmsApi.Request.Headers.Authorization!.Parameter.Should().Be("tms-ui-token");
        _tmsApi.Request.Headers.GetValues("Idempotency-Key").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WithdrawAsync_Withdrawn_IsWithdrawn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Withdrawn());

        var answer = await WithdrawAsync("CB-0000007");

        answer.Should().Be(new WithdrawAnswer.Withdrawn());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "BookingNotFound", "There is no Carrier booking CB-0000007.")]
    [InlineData(HttpStatusCode.Conflict, "BookingNotOpen", "Carrier booking CB-0000007 is Confirmed, so it can't be withdrawn.")]
    public async Task WithdrawAsync_Refused_IsRefusedWithTheTmsReason(HttpStatusCode status, string code, string reason)
    {
        _tmsApi.Answer(
            status,
            $$"""{ "status": {{(int)status}}, "title": "The Carrier booking can't be withdrawn.", "detail": "{{reason}}", "code": "{{code}}" }""",
            "application/problem+json");

        var answer = await WithdrawAsync("CB-0000007");

        answer.Should().Be(new WithdrawAnswer.Refused(reason));
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task WithdrawAsync_AnyOtherAnswer_Fails(HttpStatusCode status)
    {
        _tmsApi.Answer(status, "{}", "application/problem+json");

        var withdraw = () => WithdrawAsync("CB-0000007");

        await withdraw.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task WithdrawAsync_Withdrawn_RecordsItsOutcomeOnThePlanningTrail()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Withdrawn());

        await WithdrawAsync("CB-0000007");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-withdrawal",
            ["flow"] = "transport-planning",
            ["shipmentNumber"] = "SH-0000012",
            ["carrierBookingNumber"] = "CB-0000007",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task WithdrawAsync_Refused_RecordsItRefusedWithItsCode()
    {
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """{ "status": 409, "detail": "Already answered.", "code": "BookingNotOpen", "correlationId": "corr-refused" }""",
            "application/problem+json");

        await WithdrawAsync("CB-0000007");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-refused",
            ["flow"] = "transport-planning",
            ["carrierBookingNumber"] = "CB-0000007",
            ["outcome"] = "Refused",
            ["failureCode"] = "BookingNotOpen",
        });
    }

    [Fact]
    public async Task WithdrawAsync_SendsTheWithdrawalInASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Withdrawn());

        await WithdrawAsync("CB-0000007");

        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Withdraw Carrier booking");
    }

    [Fact]
    public async Task ReRateAsync_Rated_RecordsItsOutcomeOnTheIntakesTrail()
    {
        // A re-rate continues the Message trail of the order's intake, as its first rating does.
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        await ReRateAsync("TO-0000043");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-0043",
            ["flow"] = "transport-order-rating",
            ["partyId"] = "SHP-0001",
            ["transportOrderNumber"] = "TO-0000043",
            ["outcome"] = "Succeeded",
        }).And.NotContainKey("failureCode");
    }

    [Fact]
    public async Task ReRateAsync_FailedAgain_RecordsItSucceeded_WithTheNewFailureCode_AsTheTmsDoes()
    {
        // Rating failed is the rating's answer, not a failed flow.
        _tmsApi.Answer(HttpStatusCode.OK, Answer("RatingFailed", failure: ("WeightAboveHighestBreak", "The Lane prices up to 5,000 kg only.")));

        await ReRateAsync("TO-0000043");

        ScopeOf(_logger.LatestRecord).Should().Contain("outcome", "Succeeded").And.Contain("failureCode", "WeightAboveHighestBreak");
    }

    [Fact]
    public async Task ReRateAsync_Refused_RecordsItRefused_OnTheIntakesTrail()
    {
        _tmsApi.Answer(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409, "title": "The Transport Order can't be re-rated.",
              "detail": "Transport Order TO-0000043 is Rated; only a Transport Order whose Rating failed can be re-rated.", "correlationId": "corr-0043"
            }
            """,
            "application/problem+json");

        await ReRateAsync("TO-0000043");

        ScopeOf(_logger.LatestRecord).Should().Contain(new Dictionary<string, object?>
        {
            ["correlationId"] = "corr-0043",
            ["flow"] = "transport-order-rating",
            ["transportOrderNumber"] = "TO-0000043",
            ["outcome"] = "Refused",
        });
    }

    [Fact]
    public async Task ReRateAsync_SendsTheReRateInASpanOfItsOwn()
    {
        _tmsApi.Answer(HttpStatusCode.OK, Answer("Rated"));

        await ReRateAsync("TO-0000043");

        _spans.Should().ContainSingle().Which.DisplayName.Should().Be("Re-rate Transport Order");
    }

    private static Dictionary<string, object?> ScopeOf(FakeLogRecord record) =>
        record.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(keys => keys).ToDictionary();

    private Task<AssignAnswer> AssignAsync(string number, string? carrierId) =>
        _client.AssignAsync(number, carrierId, Xunit.TestContext.Current.CancellationToken);

    // As the TMS API answers an assignment: AssignTransportOrderResult, with its enums as strings.
    private static string Assigned(string shipmentNumber) =>
        $$"""
        {
          "shipmentNumber": "{{shipmentNumber}}",
          "transportOrderNumber": "TO-0000042",
          "status": "Planned",
          "carriedBy": { "assignedTo": "Carrier", "carrier": { "id": "CAR-0002", "name": "Kessler Spedition" } },
          "shipperId": "SHP-0001",
          "correlationId": "corr-planning",
          "repeat": false
        }
        """;

    private Task<ReRateAnswer> ReRateAsync(string number) => _client.ReRateAsync(number, Xunit.TestContext.Current.CancellationToken);

    private Task<ReassignAnswer> ReassignAsync(string shipmentNumber, string? carrierId) =>
        _client.ReassignAsync(shipmentNumber, carrierId, Xunit.TestContext.Current.CancellationToken);

    private Task<ReleaseAnswer> ReleaseWithSpotPriceAsync(string shipmentNumber, decimal amount, string remark) =>
        _client.ReleaseWithSpotPriceAsync(shipmentNumber, amount, remark, Xunit.TestContext.Current.CancellationToken);

    private Task<ReleaseAnswer> ReleaseAsync(string shipmentNumber) => _client.ReleaseAsync(shipmentNumber, Xunit.TestContext.Current.CancellationToken);

    // As the TMS API answers a reassignment: ReassignShipmentResult, with its enums as strings.
    private static string Reassigned(bool ownFleet = false) =>
        $$"""
        {
          "shipmentNumber": "SH-0000012",
          "transportOrderNumber": "TO-0000042",
          "status": "Planned",
          "carriedBy": {{(ownFleet ? """{ "assignedTo": "OwnFleet", "carrier": null }""" : """{ "assignedTo": "Carrier", "carrier": { "id": "CAR-0001", "name": "Van Dijk Transport" } }""")}},
          "previouslyCarriedBy": { "assignedTo": "Carrier", "carrier": { "id": "CAR-0002", "name": "Kessler Spedition" } },
          "shipperId": "SHP-0001",
          "correlationId": "corr-reassignment",
          "repeat": false
        }
        """;

    private Task<WithdrawAnswer> WithdrawAsync(string carrierBookingNumber) =>
        _client.WithdrawAsync(carrierBookingNumber, Xunit.TestContext.Current.CancellationToken);

    // As the TMS API answers a withdrawal: CarrierBookingWithdrawResult, with its enums as strings.
    private static string Withdrawn() =>
        """
        {
          "carrierBookingNumber": "CB-0000007",
          "status": "Withdrawn",
          "shipmentNumber": "SH-0000012",
          "shipmentStatus": "Planned",
          "correlationId": "corr-withdrawal",
          "repeat": false
        }
        """;

    // As the TMS API answers a release: ReleaseShipmentResult, with its enums as strings.
    private static string Released() =>
        """
        {
          "shipmentNumber": "SH-0000012",
          "transportOrderNumber": "TO-0000042",
          "status": "Released",
          "carriedBy": { "assignedTo": "Carrier", "carrier": { "id": "CAR-0002", "name": "Kessler Spedition" } },
          "shipperId": "SHP-0001",
          "correlationId": "corr-release",
          "repeat": false
        }
        """;

    // As the TMS API answers a re-rate: RateTransportOrderResult, with its enums as strings.
    private static string Answer(string ratingResult, (string Code, string Message)? failure = null) =>
        $$"""
        {
          "ratingResult": "{{ratingResult}}",
          "ratingFailure": {{(failure is { } f ? $$"""{ "code": "{{f.Code}}", "message": "{{f.Message}}" }""" : "null")}},
          "shipperId": "SHP-0001",
          "correlationId": "corr-0043",
          "repeat": false
        }
        """;
}
