using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>Whether a question may reach the model.</summary>
public enum Admission
{
    Allowed,

    /// <summary>Over the Persona's rate, the question size or its daily token budget: <c>AgentRateLimited</c>.</summary>
    RateLimited,

    /// <summary>The estate's daily token budget is used up, so every Persona sees "Assistant unavailable": <c>AgentUnavailable</c>.</summary>
    EstateBudgetUsed,
}

/// <summary>
/// The portal's own limits (ADR 0049), counted in memory, so the portal runs one replica while the assistant is on. The rate and size limits count
/// questions; the daily budgets count the tokens Foundry reports, and reset at midnight UTC.
/// </summary>
public sealed class AssistantLimiter(AssistantLimits limits, TimeProvider time)
{
    private readonly object _gate = new();
    private readonly Dictionary<PersonaId, Queue<DateTimeOffset>> _asked = [];
    private readonly Dictionary<PersonaId, long> _personaTokens = [];
    private long _estateTokens;
    private DateOnly _day;

    /// <summary>Checks a question and, when it is allowed, counts it against the Persona's rate.</summary>
    public Admission Admit(PersonaId persona, int questionCharacters)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            NewDay(now);
            if (_estateTokens >= limits.DailyTokensEstate)
            {
                return Admission.EstateBudgetUsed;
            }

            if (questionCharacters > limits.MaxQuestionCharacters || _personaTokens.GetValueOrDefault(persona) >= limits.DailyTokensPerPersona)
            {
                return Admission.RateLimited;
            }

            if (!_asked.TryGetValue(persona, out var times))
            {
                _asked[persona] = times = new Queue<DateTimeOffset>();
            }

            while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromHours(1))
            {
                times.Dequeue();
            }

            if (times.Count >= limits.QuestionsPerHour || times.Count(asked => now - asked < TimeSpan.FromMinutes(1)) >= limits.QuestionsPerMinute)
            {
                return Admission.RateLimited;
            }

            times.Enqueue(now);
            return Admission.Allowed;
        }
    }

    /// <summary>Counts the tokens a run used against the Persona's and the estate's daily budgets.</summary>
    public void Record(PersonaId persona, int tokens)
    {
        lock (_gate)
        {
            NewDay(time.GetUtcNow());
            _personaTokens[persona] = _personaTokens.GetValueOrDefault(persona) + tokens;
            _estateTokens += tokens;
        }
    }

    private void NewDay(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (today != _day)
        {
            _day = today;
            _personaTokens.Clear();
            _estateTokens = 0;
        }
    }
}
