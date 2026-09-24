using Norvia.Tms.Web.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.TransportOrders;

public sealed class NorviaTimeTests
{
    [Theory]
    [InlineData("2026-09-26T04:00:00Z", "2026-09-26T08:00:00Z", "Sat 26 Sep 2026, 06:00–10:00")]
    [InlineData("2026-09-26T20:00:00Z", "2026-09-27T04:00:00Z", "Sat 26 Sep 2026, 22:00 – Sun 27 Sep 2026, 06:00")]
    [InlineData("2026-12-01T07:00:00Z", "2026-12-01T09:00:00Z", "Tue 1 Dec 2026, 08:00–10:00")]
    [InlineData("2026-09-26T04:00:00Z", null, "From Sat 26 Sep 2026, 06:00")]
    [InlineData(null, "2026-09-26T08:00:00Z", "Until Sat 26 Sep 2026, 10:00")]
    [InlineData(null, null, "Not given")]
    public void FormatWindow_InRotterdamTime_ReadsAsAPlannerExpects(string? earliest, string? latest, string expected)
    {
        NorviaTime.FormatWindow(Parse(earliest), Parse(latest)).Should().Be(expected);
    }

    [Fact]
    public void Format_NoMoment_IsNotGiven()
    {
        NorviaTime.Format(null).Should().Be("Not given");
    }

    private static DateTimeOffset? Parse(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
