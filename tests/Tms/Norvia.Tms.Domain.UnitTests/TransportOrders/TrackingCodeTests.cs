using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Domain.TransportOrders;

namespace Norvia.Tms.Domain.UnitTests.TransportOrders;

public sealed class TrackingCodeTests
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [Fact]
    public void Generate_Code_Is16CharactersOfCrockfordBase32()
    {
        var code = TrackingCode.Generate();

        code.Value.Should().HaveLength(16).And.MatchRegex("^[0-9A-HJKMNP-TV-Z]{16}$");
        code.Value.All(character => Alphabet.Contains(character, StringComparison.Ordinal)).Should().BeTrue();
    }

    [Fact]
    public void Generate_ManyCodes_AreDifferent()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => TrackingCode.Generate().Value).ToHashSet();

        codes.Should().HaveCount(500);
    }

    [Fact]
    public void ToString_NeverShowsTheCode()
    {
        var code = TrackingCode.Generate();

        code.ToString().Should().NotContain(code.Value);
    }

    [Fact]
    public void TryParse_LowerCaseAndLookAlikes_IsTheSameCode()
    {
        TrackingCode.TryParse("abcdefghjkmnpqrs", out var lower).Should().BeTrue();
        TrackingCode.TryParse("0123456789ABCDEF", out var canonical).Should().BeTrue();

        lower!.Value.Should().Be("ABCDEFGHJKMNPQRS");
        TrackingCode.TryParse("OIl3456789ABCDEF", out var aliased).Should().BeTrue();
        aliased!.Value.Should().Be("0113456789ABCDEF");
        canonical!.Value.Should().Be("0123456789ABCDEF");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("0123456789ABCDEFG")]
    [InlineData("0123456789ABCDEU")]
    [InlineData("0123456789ABCD-F")]
    public void TryParse_NotACode_IsRefused(string? text)
    {
        TrackingCode.TryParse(text, out var code).Should().BeFalse();

        code.Should().BeNull();
    }

    [Fact]
    public void ExpiresAt_IsThirtyDaysAfterTheBasis()
    {
        var basis = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        TrackingCode.ExpiresAt(basis).Should().Be(basis.AddDays(30));
    }

    [Fact]
    public void ExpiresAt_CountsFromDelivery_ThenLastProgress_ThenReceipt()
    {
        var received = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var progress = received.AddDays(2);
        var delivered = received.AddDays(3);

        TrackingCode.ExpiresAt(delivered, progress, received).Should().Be(delivered.AddDays(30));
        TrackingCode.ExpiresAt(null, progress, received).Should().Be(progress.AddDays(30));
        TrackingCode.ExpiresAt(null, null, received).Should().Be(received.AddDays(30));
    }

    [Fact]
    public void Accept_TransportOrder_HasATrackingCode()
    {
        var order = Accepted();

        order.TrackingCode.Should().NotBeNull();
    }

    [Fact]
    public void Accept_TwoTransportOrders_HaveDifferentTrackingCodes()
    {
        Accepted().TrackingCode.Should().NotBe(Accepted().TrackingCode);
    }

    [Fact]
    public void Reject_Booking_HasNoTrackingCode()
    {
        var order = TransportOrder.Reject(
            new ShipperId("SHP-0001"),
            TransportOrderTests.SampleDetails(),
            [new(RejectionCode.GoodsLinesMissing, "No goods.")],
            IntakeChannel.ShipperApi,
            new CorrelationId("corr-1"),
            DateTimeOffset.UnixEpoch);

        order.TrackingCode.Should().BeNull();
        order.ReplaceTrackingCode().Should().BeNull();
    }

    [Fact]
    public void ReplaceTrackingCode_AcceptedTransportOrder_GetsANewCodeAndTheOldOneIsGone()
    {
        var order = Accepted();
        var old = order.TrackingCode;

        var replacement = order.ReplaceTrackingCode();

        replacement.Should().NotBeNull().And.NotBe(old);
        order.TrackingCode.Should().Be(replacement);
    }

    private static TransportOrder Accepted() => TransportOrder.Accept(
        new ShipperId("SHP-0001"),
        TransportOrderTests.SampleDetails(),
        TransportOrderNumber.FromSequence(1),
        IntakeChannel.ShipperApi,
        new CorrelationId("corr-1"),
        DateTimeOffset.UnixEpoch);
}
