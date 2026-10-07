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

    [Theory]
    [InlineData("2026-10-08 14:30", "2026-10-08T12:30:00Z")]
    [InlineData(" 2026-12-01 08:00 ", "2026-12-01T07:00:00Z")]
    public void TryParse_ATimeAsAPlannerTypesIt_IsRotterdamLocalTime(string text, string expected)
    {
        NorviaTime.TryParse(text, out var moment).Should().BeTrue();

        moment.Should().Be(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("08/10/2026 14:30")]
    [InlineData("2026-10-08")]
    [InlineData("2026-03-29 02:30")] // doesn't exist in Rotterdam: the clocks went forward
    public void TryParse_AnythingElse_IsNotATime(string? text)
    {
        NorviaTime.TryParse(text, out _).Should().BeFalse();
    }

    private static DateTimeOffset? Parse(string? value) =>
        value is null ? null : DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
