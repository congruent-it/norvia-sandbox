using Norvia.PartnerPortal.Application.Bookings.DraftFromText;
using Norvia.PartnerPortal.Web.Bookings;

namespace Norvia.PartnerPortal.Web.UnitTests.Bookings;

public sealed class BookingDraftHandoffTests
{
    [Fact]
    public void Take_GivesTheDraftOnce()
    {
        var handoff = new BookingDraftHandoff();
        var draft = BookingDraftAnswer.Refused(BookingDraftOutcome.Drafted, "corr-1");
        handoff.Put(draft);

        handoff.Take().Should().Be(draft);
        handoff.Take().Should().BeNull("the draft is not kept once the form has it");
    }

    [Theory]
    [InlineData("pickupFrom", "Collect from")]
    [InlineData("receiverName", "Consignee")]
    [InlineData("items.0.weight", "Goods line 1, weight")]
    [InlineData("items.2.count", "Goods line 3, quantity")]
    [InlineData("something.else", "something.else")]
    public void DraftFieldLabels_NameAFieldAsTheFormDoes(string field, string label)
    {
        DraftFieldLabels.Of(field).Should().Be(label);
    }
}
