using Microsoft.EntityFrameworkCore;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Infrastructure.Data;

namespace Norvia.PartnerPortal.Infrastructure.UnitTests.Data;

/// <summary>How a booking's answer is stored, checked on the model alone: no database is involved.</summary>
public sealed class BookingConfigurationTests
{
    [Theory]
    [MemberData(nameof(EveryAnswer))]
    public void Answer_IsReadBackAsItWasStored(NorviaAnswer answer)
    {
        using var context = new PartnerPortalDbContext(new DbContextOptionsBuilder<PartnerPortalDbContext>().UseSqlServer("Server=unused").Options);
        var converter = context.Model.FindEntityType(typeof(Booking))!.FindProperty(nameof(Booking.Answer))!.GetValueConverter()!;

        var stored = converter.ConvertToProvider(answer);
        var read = (NorviaAnswer)converter.ConvertFromProvider(stored)!;

        read.Result.Should().Be(answer.Result);
        read.MessageId.Should().Be(answer.MessageId);
        read.TransportOrderNumber.Should().Be(answer.TransportOrderNumber);
        read.Reasons.Should().Equal(answer.Reasons);
        read.Text.Should().Be(answer.Text);
    }

    public static TheoryData<NorviaAnswer> EveryAnswer() =>
    [
        NorviaAnswer.Accepted("TO-0000042", "corr-1"),
        NorviaAnswer.AlreadyBooked("TO-0000042", "corr-2"),
        NorviaAnswer.Rejected([new RejectionReason("QuantityInvalid", "Goods line 1 needs a quantity of at least 1."), new RejectionReason("WeightInvalid", "Goods line 1 needs a weight greater than 0 kg.")], "corr-3"),
        NorviaAnswer.NotReceived(null),
        NorviaAnswer.Refused("This client may not submit Transport Orders.", "corr-5"),
    ];
}
