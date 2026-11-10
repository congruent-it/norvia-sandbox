using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

/// <summary>What the review screen knows of a Booking draft: the quote of each field, the flags, whether the Shipper has handled them, and what was ignored.</summary>
public sealed class DraftReviewTests
{
    private static BookingDraftAnswer Draft(BookingRequest booking, IEnumerable<FlaggedField>? flags = null, IEnumerable<string>? ignored = null) =>
        new(
            BookingDraftOutcome.Drafted,
            booking,
            [.. flags ?? []],
            "corr-1",
            new Dictionary<string, string> { ["pickupCity"] = "magazijn in Utrecht", ["items.0.count"] = "3 pallets" },
            [.. ignored ?? []]);

    private static BookingRequest Booking() => new()
    {
        CollectionAddress = new BookingAddress(City: "Utrecht"),
        GoodsLines = [new BookingGoodsLine(PackageType.Pallet, 3, null)],
    };

    [Fact]
    public void QuoteOf_AFilledField_IsItsQuote_AndAnEmptyOneHasNone()
    {
        var review = new DraftReview(Draft(Booking()));

        review.QuoteOf("pickupCity").Should().Be("magazijn in Utrecht");
        review.QuoteOf("items.0.count").Should().Be("3 pallets");
        review.QuoteOf("bookingRef").Should().BeNull();
    }

    [Fact]
    public void Flags_AFieldTheTextDidNotGive_IsMissing_AndOneTheAgentWasUnsureOf_IsToCheck()
    {
        var review = new DraftReview(Draft(
            Booking(),
            [new FlaggedField("bookingRef", "The text gives no reference."), new FlaggedField("pickupCity", "Two cities are named.", CheckThis: true)]));

        review.Flags.Should().Equal(
            new DraftFlag("bookingRef", "The text gives no reference.", DraftFlagKind.Missing),
            new DraftFlag("pickupCity", "Two cities are named.", DraftFlagKind.CheckThis));
        review.FlagOf("bookingRef")!.Kind.Should().Be(DraftFlagKind.Missing);
        review.FlagOf("pickupStreet").Should().BeNull();
    }

    [Fact]
    public void CanSubmit_WithoutFlags_IsTrue()
    {
        new DraftReview(Draft(Booking())).CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void CanSubmit_WithAFlaggedFieldNobodyHandled_IsFalse_AndNamesIt()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("bookingRef", "No reference.")]));

        review.CanSubmit.Should().BeFalse();
        review.Unhandled.Select(flag => flag.Field).Should().Equal("bookingRef");
    }

    [Fact]
    public void Confirm_HandlesAFlag_AndTellsTheScreen()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("pickupCity", "Two cities.")]));
        var changes = 0;
        review.Changed += () => changes++;

        review.Confirm("pickupCity");

        review.CanSubmit.Should().BeTrue();
        review.IsHandled("pickupCity").Should().BeTrue();
        changes.Should().Be(1);
    }

    [Fact]
    public void Observe_AFlaggedFieldThatWasFilledIn_IsHandled()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("bookingRef", "No reference.")]));

        review.Observe(Booking() with { ShipperReference = "SH-1" });

        review.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Observe_AFlaggedFieldThatWasEdited_IsHandled_UntilItIsEditedBack()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("pickupCity", "Two cities.", CheckThis: true)]));

        review.Observe(Booking() with { CollectionAddress = new BookingAddress(City: "Amersfoort") });
        review.CanSubmit.Should().BeTrue();

        review.Observe(Booking());
        review.CanSubmit.Should().BeFalse("the value is the draft's again and nobody confirmed it");
    }

    [Fact]
    public void Observe_AConfirmedField_StaysHandledWhateverTheFormHolds()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("pickupCity", "Two cities.", CheckThis: true)]));
        review.Confirm("pickupCity");

        review.Observe(Booking() with { CollectionAddress = new BookingAddress(City: "Amersfoort") });
        review.Observe(Booking());

        review.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Observe_AnUntouchedFlaggedField_StaysUnhandled()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("bookingRef", "No reference.")]));

        review.Observe(Booking() with { Instructions = "Call first" });

        review.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public void Observe_AFlaggedGoodsLineFieldWhoseLineWasRemoved_IsHandled()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("items.0.weight", "Two weights.")]));

        review.Observe(Booking() with { GoodsLines = [] });

        review.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Observe_AFlaggedGoodsLineWeightThatWasFilledIn_IsHandled()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("items.0.weight", "Ambiguous.")]));

        review.Observe(Booking() with { GoodsLines = [new BookingGoodsLine(PackageType.Pallet, 3, 850m)] });

        review.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Observe_AFlagOnTheWholeBooking_NeedsAConfirmation()
    {
        var review = new DraftReview(Draft(new BookingRequest(), [new FlaggedField("booking", "No booking in the text.")]));

        review.Observe(new BookingRequest { ShipperReference = "SH-1" });
        review.CanSubmit.Should().BeFalse();

        review.Confirm("booking");
        review.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Observe_NotesTheChange_OnlyWhenAFlagGetsHandled()
    {
        var review = new DraftReview(Draft(Booking(), [new FlaggedField("bookingRef", "No reference.")]));
        var changes = 0;
        review.Changed += () => changes++;

        review.Observe(Booking());
        review.Observe(Booking() with { ShipperReference = "SH-1" });
        review.Observe(Booking() with { ShipperReference = "SH-1" });

        changes.Should().Be(1);
    }

    [Fact]
    public void Ignored_IsWhatTheAgentDidNotUse()
    {
        var review = new DraftReview(Draft(Booking(), ignored: ["Groet, Jan"]));

        review.Ignored.Should().Equal("Groet, Jan");
    }
}
