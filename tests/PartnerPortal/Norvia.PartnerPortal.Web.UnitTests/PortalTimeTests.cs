namespace Norvia.PartnerPortal.Web.UnitTests;

public sealed class PortalTimeTests
{
    [Fact]
    public void Format_IsTheTimeInRotterdam()
    {
        PortalTime.Format(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)).Should().Be("Fri 25 Sep 2026, 12:00");
    }
}
