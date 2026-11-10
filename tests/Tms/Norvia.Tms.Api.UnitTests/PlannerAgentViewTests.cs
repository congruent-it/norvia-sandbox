using System.Text.Json;
using Norvia.Tms.Api.Agents;
using Norvia.Tms.Application.Agents.Planner;
using Norvia.Tms.Application.Fleet.Planning;
using Norvia.Tms.Application.TransportOrders;
using Norvia.Tms.Domain.Shipments;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Api.UnitTests;

/// <summary>The Planner agent's view is the order narrowed and the options, and nothing more: no street, no Consignee, no file (#440, ADR 0040).</summary>
public sealed class PlannerAgentViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 11);

    private static AssignmentOptions Options(OwnFleetOption? ownFleet = null) => new(
        "TO-0000042",
        TransportOrderStatus.Accepted,
        "Brouwer Dranken",
        RatingResult.Rated,
        new PlaceView("3087 BM", "Rotterdam", "NL"),
        new PlaceView("3511 AW", "Utrecht", "NL"),
        Now.AddDays(1),
        Now.AddDays(1).AddHours(4),
        Now.AddDays(1).AddHours(6),
        null,
        910.5m,
        2,
        ownFleet ?? new OwnFleetOption(
            null, OwnFleetFit.OverCapacity, Day,
            new LoadCheckWarning(Day, false, new DayLoadView(30000, 30, 3400, 34, true)), new DayLoadView(30000, 30, 3400, 34, true),
            1, 1, 1, TimeSpan.FromHours(7.5)),
        [
            new CarrierOption("CAR-0001", "Van Dijk Transport", null, 112.90m, null),
            new CarrierOption("CAR-0002", "Kessler Spedition", null, null, AssignmentRefusalCode.NoMatchingLane),
            new CarrierOption("CAR-0003", "Closed", new AssignmentRefusal(AssignmentRefusalCode.InactiveCarrier, "Carrier CAR-0003 (Closed) is not active, so it can't be chosen."), null, null),
        ]);

    [Fact]
    public void Of_IsThePlaceAsCityPostcodeAreaAndCountry_TheGoodsAsTotals_TheWindows_TheShipperAndTheRatingResult()
    {
        var view = PlannerAgentMapping.Of(Options(), Now);

        view.Collection.Should().Be(new PlannerAgentPlaceView("Rotterdam", "30", "NL"));
        view.Delivery.Should().Be(new PlannerAgentPlaceView("Utrecht", "35", "NL"));
        view.Goods.Should().Be(new PlannerAgentGoodsView(910.5m, 2));
        view.Shipper.Should().Be("Brouwer Dranken");
        view.RatingResult.Should().Be("Rated");
        view.CollectionWindow.Should().Be(new PlannerAgentWindowView(Now.AddDays(1), Now.AddDays(1).AddHours(4)));
        view.DeliveryWindow.Should().Be(new PlannerAgentWindowView(Now.AddDays(1).AddHours(6), null));
        view.AsOf.Should().Be(Now);
    }

    [Fact]
    public void Of_OwnFleet_IsTheFitTheWarningTheLoadAndTheDrivingHours()
    {
        var own = PlannerAgentMapping.Of(Options(), Now).OwnFleet;

        own.MeetsAssignmentRules.Should().BeTrue();
        own.Fit.Should().Be("OverCapacity");
        own.Warning.Should().Be("Wed 11 Nov: Over capacity, 34 of 30 pallet places after this assignment");
        own.Load.Should().Be(new PlannerAgentLoadView(30000, 30, 3400, 34, true));
        own.MostDrivingHoursLeft.Should().Be(7.5);
    }

    [Fact]
    public void Of_ACarrier_IsItsCostOrWhyItCannotBePriced_AndWhetherItMeetsTheRules()
    {
        var carriers = PlannerAgentMapping.Of(Options(), Now).Carriers;

        carriers[0].Should().Be(new PlannerAgentCarrierView("CAR-0001", "Van Dijk Transport", true, null, 112.90m, null));
        carriers[1].Should().Be(new PlannerAgentCarrierView("CAR-0002", "Kessler Spedition", true, null, null, "NoMatchingLane"));
        carriers[2].MeetsAssignmentRules.Should().BeFalse();
        carriers[2].RuleBroken.Should().Be("InactiveCarrier");
    }

    [Fact]
    public void Of_AnOrderPlannedAlready_MakesOwnFleetRefusedByTheRule()
    {
        var own = PlannerAgentMapping.Of(
            Options(new OwnFleetOption(new AssignmentRefusal(AssignmentRefusalCode.TransportOrderAlreadyPlanned, "planned"), OwnFleetFit.Fits, Day, null, null, 0, 0, 0, null)), Now).OwnFleet;

        own.MeetsAssignmentRules.Should().BeFalse();
        own.RuleBroken.Should().Be("TransportOrderAlreadyPlanned");
        own.Load.Should().BeNull();
    }

    [Fact]
    public void Of_NeverCarriesAStreetAFullPostcodeAConsigneeAnInstructionOrAFile()
    {
        var json = JsonSerializer.Serialize(PlannerAgentMapping.Of(Options(), Now));

        json.Should().NotContainAny("3087", "BM", "3511", "AW", "Waalhaven", "Oudegracht", "street", "Street", "consignee", "Consignee", "contact", "phone", "email", "instruction", "Instruction", "file", "File");
        json.Should().NotContain("planned", "the refusal's message holds names and IDs the agent has no use for");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("3", "3")]
    [InlineData("1234 ab", "12")]
    [InlineData("SW1A 1AA", "SW")]
    public void Of_ThePostcodeArea_IsItsFirstTwoCharacters(string? postcode, string? area)
    {
        var options = Options() with { Collection = new PlaceView(postcode, "Anywhere", "NL") };

        PlannerAgentMapping.Of(options, Now).Collection!.PostcodeArea.Should().Be(area);
    }

    [Fact]
    public void Of_NoPlacesAndNoWindows_AreNull()
    {
        var options = Options() with { Collection = null, Delivery = null, CollectionEarliest = null, CollectionLatest = null, DeliveryEarliest = null, DeliveryLatest = null };

        var view = PlannerAgentMapping.Of(options, Now);

        view.Collection.Should().BeNull();
        view.Delivery.Should().BeNull();
        view.CollectionWindow.Should().BeNull();
        view.DeliveryWindow.Should().BeNull();
    }

    [Fact]
    public void TheView_IsAnAllowlist_SoANewFieldIsExcludedUntilSomeoneAddsItHere()
    {
        var names = typeof(PlannerAgentAssignmentOptionsView).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(PlannerAgentAssignmentOptionsView).Namespace && type.Name.StartsWith("PlannerAgent", StringComparison.Ordinal) && type.Name.EndsWith("View", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToList();

        names.Should().BeEquivalentTo(
            [
                "TransportOrderNumber", "Status", "Shipper", "RatingResult", "AsOf", "Collection", "Delivery", "CollectionWindow", "DeliveryWindow", "Goods", "OwnFleet", "Carriers",
                "City", "PostcodeArea", "Country",
                "Earliest", "Latest",
                "TotalWeightKg", "PalletPlaces",
                "MeetsAssignmentRules", "RuleBroken", "Fit", "CollectionDay", "Warning", "Load", "AvailableVehicles", "AvailableDrivers", "DriversWithDrivingHoursLeft", "MostDrivingHoursLeft",
                "CapacityKg", "CapacityPalletPlaces", "DemandKg", "DemandPalletPlaces", "Over",
                "CarrierId", "Name", "MeetsAssignmentRules", "RuleBroken", "CarrierCost", "NotPriced",
            ]);
    }
}
