using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Application.UnitTests.TrackingAssistant;

public sealed class TrackingFetchPlanTests
{
    private static readonly IReadOnlyList<Norvia.PartnerPortal.Application.ShipperTracking.ShipperTrackingSummary> Listed =
        [TrackingFixtures.Summary("TO-0000042", "BD-2030-0001")];

    [Fact]
    public void Extra_ForAQuestionThatNamesNothing_FetchesNothing()
    {
        TrackingFetchPlan.Extra("Where is my order?", null, Listed).Should().BeNull();
    }

    [Fact]
    public void Extra_ForAReferenceAlreadyInTheList_FetchesNothing()
    {
        TrackingFetchPlan.Extra("What about bd-2030-0001?", null, Listed).Should().BeNull();
    }

    [Fact]
    public void Extra_ForANumberAlreadyInTheOpenOrder_FetchesNothing()
    {
        TrackingFetchPlan.Extra("Is TO-0000042 late?", TrackingFixtures.Detail, []).Should().BeNull();
    }

    [Fact]
    public void Extra_ForAReferenceInNeither_FindsItOnce()
    {
        TrackingFetchPlan.Extra("Where is SH-2030-0142 and BD-2030-0777?", null, Listed)
            .Should().Be(new ExtraFetch(ExtraFetchKind.FindReference, "SH-2030-0142"));
    }

    [Fact]
    public void Extra_ForANumberInNeither_GetsItByNumber()
    {
        TrackingFetchPlan.Extra("Status of to-0000099 please", null, Listed)
            .Should().Be(new ExtraFetch(ExtraFetchKind.GetNumber, "TO-0000099"));
    }

    [Fact]
    public void Extra_DoesNotTakeADateOrATimeForAReference()
    {
        TrackingFetchPlan.Extra("Will it arrive before 02-10-2030 or 12:30?", null, Listed).Should().BeNull();
    }
}
