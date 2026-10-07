using Norvia.PartnerPortal.Domain.Personas;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Domain.UnitTests.ShipperNotifications;

public sealed class ShipperNotificationTests
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly PersonaId Brouwer = new("brouwer-dranken");
    private static readonly Guid FirstId = Guid.Parse("0f1c0d9e-3b0a-4f43-a6f6-7f1f0d6a9b01");
    private static readonly Guid SecondId = Guid.Parse("0f1c0d9e-3b0a-4f43-a6f6-7f1f0d6a9b02");

    private static readonly ShipperNotificationContent Content = new(
        "BD-2030-0001", "TO-0000042", ShipperExceptionCode.Late, "The truck is stuck in traffic.", "03-10-2030 14:30", null, null, "Eva Planner");

    private static ShipperNotification Receive(PersonaId? persona = null, Guid? id = null, DateTimeOffset? at = null) =>
        ShipperNotification.Receive(persona ?? Brouwer, id ?? FirstId, "corr-1", at ?? ReceivedAt, Content);

    [Fact]
    public void Receive_KeepsWhatNorviaSaid()
    {
        var notification = Receive();

        notification.PersonaId.Should().Be(Brouwer);
        notification.NotificationId.Should().Be(FirstId);
        notification.CorrelationId.Should().Be("corr-1");
        notification.ReceivedAt.Should().Be(ReceivedAt);
        notification.Content.Should().BeSameAs(Content);
    }

    [Fact]
    public void Receive_GivesEachNotificationAnIdOfItsOwn() =>
        Receive().Id.Should().NotBe(Receive().Id);

    [Fact]
    public void Receive_WithoutAPersonaOrContent_Throws()
    {
        var withoutPersona = () => ShipperNotification.Receive(null!, FirstId, "corr-1", ReceivedAt, Content);
        var withoutContent = () => ShipperNotification.Receive(Brouwer, FirstId, "corr-1", ReceivedAt, null!);

        withoutPersona.Should().Throw<ArgumentNullException>();
        withoutContent.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Receive_WithoutANotificationIdOrACorrelationId_Throws()
    {
        var withoutId = () => ShipperNotification.Receive(Brouwer, Guid.Empty, "corr-1", ReceivedAt, Content);
        var withoutCorrelation = () => ShipperNotification.Receive(Brouwer, FirstId, " ", ReceivedAt, Content);

        withoutId.Should().Throw<ArgumentOutOfRangeException>();
        withoutCorrelation.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ShipperNotificationsOfPersonaSpec_ListsOnlyThePersonasNotifications_NewestFirst()
    {
        var earlier = Receive(at: ReceivedAt);
        var later = Receive(id: SecondId, at: ReceivedAt.AddMinutes(5));
        var someoneElses = Receive(persona: new PersonaId("tuincentrum-groenrijk"), at: ReceivedAt.AddMinutes(9));

        var listed = new ShipperNotificationsOfPersonaSpec(Brouwer).Evaluate([earlier, someoneElses, later]);

        listed.Should().Equal(later, earlier);
    }

    [Fact]
    public void ShipperNotificationByNotificationIdSpec_FindsThePersonasNotificationWithThatId()
    {
        var wanted = Receive();
        var otherId = Receive(id: SecondId);
        var otherPersona = Receive(persona: new PersonaId("tuincentrum-groenrijk"));

        var found = new ShipperNotificationByNotificationIdSpec(Brouwer, FirstId).Evaluate([otherId, otherPersona, wanted]);

        found.Should().Equal(wanted);
    }

    [Theory]
    [InlineData("COLLECTION_FAILED", ShipperExceptionCode.CollectionFailed)]
    [InlineData("DELIVERY_FAILED", ShipperExceptionCode.DeliveryFailed)]
    [InlineData("DAMAGED", ShipperExceptionCode.Damaged)]
    [InlineData("RESERVATIONS", ShipperExceptionCode.Reservations)]
    [InlineData("LATE", ShipperExceptionCode.Late)]
    public void TryParse_AKnownCode_IsTheExceptionCode(string wire, ShipperExceptionCode expected)
    {
        ShipperExceptionCodes.TryParse(wire, out var code).Should().BeTrue();
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("late")]
    [InlineData("STOLEN")]
    public void TryParse_AnythingElse_IsNotACode(string? wire) =>
        ShipperExceptionCodes.TryParse(wire, out _).Should().BeFalse();
}
