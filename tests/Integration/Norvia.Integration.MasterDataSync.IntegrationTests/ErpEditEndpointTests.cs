using System.Net;
using System.Text.Json.Nodes;

namespace Norvia.Integration.MasterDataSync.IntegrationTests;

/// <summary>
/// The ERP API's master-data saves (<c>PUT /v1/{entity}/{id}</c>) of all six entities, against the real ERP and its database: what Finance's form sends through the ERP UI's own
/// identity, which alone holds <c>MasterData.Manage</c>. The saves share the ERP; each test makes records of its own.
/// </summary>
public sealed class ErpEditEndpointTests(MasterDataSyncFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> KindNames => Kinds.Names;

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task ANewRecord_IsCreated_AtRevision1_AndTheCreatedNamesItsRoute(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();

        var saved = await fixture.SaveAsync(kind, id, await kind.NewBody(fixture, id, Ct), ct: Ct);

        saved.Status.Should().Be(HttpStatusCode.Created, saved.Body?.ToJsonString());
        saved.Outcome.Should().Be("Created");
        saved.Body![kind.RecordName]!["revision"]!.GetValue<int>().Should().Be(1);
        saved.Location!.ToString().Should().EndWith($"{kind.Route}/{id}");
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task AnEditedRecord_IsUpdated_AtTheNextRevision(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        await fixture.SaveAsync(kind, id, body, ct: Ct);
        kind.Change(body);

        var saved = await fixture.SaveAsync(kind, id, body, ct: Ct);

        saved.Status.Should().Be(HttpStatusCode.OK, saved.Body?.ToJsonString());
        saved.Outcome.Should().Be("Updated");
        saved.Body![kind.RecordName]!["revision"]!.GetValue<int>().Should().Be(2);
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task ARepeatOfWhatTheRecordIs_ChangesNothing_AndKeepsTheRevision(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        await fixture.SaveAsync(kind, id, body, ct: Ct);

        var repeated = await fixture.SaveAsync(kind, id, body, ct: Ct);

        repeated.Status.Should().Be(HttpStatusCode.OK);
        repeated.Outcome.Should().Be("Unchanged");
        repeated.Body![kind.RecordName]!["revision"]!.GetValue<int>().Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task ASaveSentAgainWithTheSameKey_ChangesNothing_EvenWhenTheBodyDiffers(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        var key = Guid.NewGuid().ToString();
        (await fixture.SaveAsync(kind, id, body, key, ct: Ct)).Status.Should().Be(HttpStatusCode.Created);
        kind.Change(body);

        var again = await fixture.SaveAsync(kind, id, body, key, ct: Ct);

        again.Status.Should().Be(HttpStatusCode.OK, again.Body?.ToJsonString());
        again.Outcome.Should().Be("Repeat");
        again.Body![kind.RecordName]!["revision"]!.GetValue<int>().Should().Be(1, "the key's save already made the record; the retry made no new revision");
    }

    [Theory]
    [MemberData(nameof(KindNames))]
    public async Task ACallerWithoutTheManageRole_IsRefused403_AndNothingIsSaved(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        using var finance = fixture.ErpAs("Finance");

        var refused = await fixture.SaveAsync(kind, id, body, caller: finance, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.SaveAsync(kind, id, body, ct: Ct)).Outcome.Should().Be("Created", "the refused call saved nothing, so this one makes the record");
    }

    [Fact]
    public async Task ACallerWithNoToken_IsRefused401()
    {
        var kind = Kinds.Shipper;
        var id = kind.NewId();
        using var anonymous = fixture.ErpAs(roles: null);

        var refused = await fixture.SaveAsync(kind, id, await kind.NewBody(fixture, id, Ct), caller: anonymous, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AVehicleWithARegistrationAnotherVehicleHolds_IsRefused400_AndKeepsItsRevision()
    {
        var kind = Kinds.Vehicle;
        var first = kind.NewId();
        var second = kind.NewId();
        var firstBody = await kind.NewBody(fixture, first, Ct);
        var secondBody = await kind.NewBody(fixture, second, Ct);
        await fixture.SaveAsync(kind, first, firstBody, ct: Ct);
        await fixture.SaveAsync(kind, second, secondBody, ct: Ct);
        var clash = (JsonObject)secondBody.DeepClone();
        clash["registration"] = firstBody["registration"]!.GetValue<string>();

        var refused = await fixture.SaveAsync(kind, second, clash, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        refused.Code.Should().Be("VehicleRegistrationTaken");
        (await fixture.SaveAsync(kind, second, secondBody, ct: Ct)).Body![kind.RecordName]!["revision"]!.GetValue<int>().Should().Be(1);
    }

    [Theory]
    [InlineData("shippers")]
    [InlineData("carriers")]
    [InlineData("drivers")]
    public async Task ARecordWithNoName_IsRefused400(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        body["name"] = " ";

        var refused = await fixture.SaveAsync(kind, id, body, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        refused.Code.Should().Be("MasterDataFieldRequired");
    }

    [Theory]
    [InlineData("rate-cards")]
    [InlineData("carrier-rate-cards")]
    public async Task ACardWithVersionsThatOverlapInTime_IsRefused400(string kindName)
    {
        var kind = Kinds.Named(kindName);
        var id = kind.NewId();
        var body = await kind.NewBody(fixture, id, Ct);
        body["versions"] = new JsonArray(Cards.Version(1, "2026-01-01", "2026-12-31", 65m), Cards.Version(2, "2026-06-01", null, 75m));

        var refused = await fixture.SaveAsync(kind, id, body, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        refused.Code.Should().Be("RateCardVersionsOverlap");
    }

    [Fact]
    public async Task AShipperThatAlreadyHasACard_IsRefusedASecondOne_WithCode400()
    {
        var kind = Kinds.RateCard;
        var body = await kind.NewBody(fixture, kind.NewId(), Ct);
        (await fixture.SaveAsync(kind, kind.NewId(), body, ct: Ct)).Status.Should().Be(HttpStatusCode.Created);

        var second = await fixture.SaveAsync(kind, kind.NewId(), body, ct: Ct);

        second.Status.Should().Be(HttpStatusCode.BadRequest);
        second.Code.Should().Be("RateCardShipperHasCard");
    }

    [Fact]
    public async Task ACardOfAShipperTheErpDoesNotKnow_IsRefused400()
    {
        var kind = Kinds.RateCard;
        var body = await kind.NewBody(fixture, kind.NewId(), Ct);
        body["shipperId"] = "SHP-NOBODY";

        var refused = await fixture.SaveAsync(kind, kind.NewId(), body, ct: Ct);

        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        refused.Code.Should().Be("RateCardPartyUnknown");
    }
}
