using Microsoft.Extensions.Time.Testing;
using Norvia.PartnerPortal.Application.TrackingAssistant;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.UnitTests.TrackingAssistant;

public sealed class AssistantLimiterTests
{
    private static readonly PersonaId Brouwer = new("brouwer-dranken");
    private static readonly PersonaId Other = new("other-shipper");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2030, 10, 1, 10, 0, 0, TimeSpan.Zero));

    private AssistantLimiter Limiter(AssistantLimits? limits = null) => new(limits ?? new AssistantLimits(), _time);

    [Fact]
    public void Admit_AllowsAQuestionWithinEveryLimit()
    {
        Limiter().Admit(Brouwer, 100).Should().Be(Admission.Allowed);
    }

    [Fact]
    public void Admit_RefusesTheSeventhQuestionInAMinute_AndAllowsItWhenTheMinuteHasPassed()
    {
        var limiter = Limiter();
        for (var i = 0; i < 6; i++)
        {
            limiter.Admit(Brouwer, 10).Should().Be(Admission.Allowed);
        }

        limiter.Admit(Brouwer, 10).Should().Be(Admission.RateLimited);
        limiter.Admit(Other, 10).Should().Be(Admission.Allowed);

        _time.Advance(TimeSpan.FromSeconds(61));
        limiter.Admit(Brouwer, 10).Should().Be(Admission.Allowed);
    }

    [Fact]
    public void Admit_RefusesTheSixtyFirstQuestionInAnHour()
    {
        var limiter = Limiter();
        for (var i = 0; i < 60; i++)
        {
            limiter.Admit(Brouwer, 10).Should().Be(Admission.Allowed);
            _time.Advance(TimeSpan.FromSeconds(11));
        }

        limiter.Admit(Brouwer, 10).Should().Be(Admission.RateLimited);
    }

    [Fact]
    public void Admit_RefusesAQuestionOverTheSizeLimit_WithoutCountingIt()
    {
        var limiter = Limiter();

        limiter.Admit(Brouwer, 501).Should().Be(Admission.RateLimited);
        for (var i = 0; i < 6; i++)
        {
            limiter.Admit(Brouwer, 500).Should().Be(Admission.Allowed);
        }
    }

    [Fact]
    public void Admit_RefusesAPersonaWhoseDailyTokensAreUsedUp_UntilMidnightUtc()
    {
        var limiter = Limiter();
        limiter.Record(Brouwer, 50_000);

        limiter.Admit(Brouwer, 10).Should().Be(Admission.RateLimited);
        limiter.Admit(Other, 10).Should().Be(Admission.Allowed);

        _time.Advance(TimeSpan.FromHours(14));
        limiter.Admit(Brouwer, 10).Should().Be(Admission.Allowed);
    }

    [Fact]
    public void Admit_MakesEveryPersonaSeeUnavailable_WhenTheEstateBudgetIsUsedUp()
    {
        var limiter = Limiter();
        for (var i = 0; i < 10; i++)
        {
            limiter.Record(new PersonaId($"shipper-{i}"), 50_000);
        }

        limiter.Admit(Brouwer, 10).Should().Be(Admission.EstateBudgetUsed);

        _time.Advance(TimeSpan.FromHours(14));
        limiter.Admit(Brouwer, 10).Should().Be(Admission.Allowed);
    }
}
