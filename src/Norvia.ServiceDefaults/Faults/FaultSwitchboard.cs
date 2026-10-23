using Microsoft.Extensions.Options;

namespace Norvia.ServiceDefaults.Faults;

internal sealed class FaultSwitchboard : IFaults
{
    private readonly Dictionary<string, FaultDefinition> _definitions;
    private readonly Dictionary<string, DateTimeOffset> _until = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly FaultOptions _options;

    public FaultSwitchboard(IEnumerable<FaultDefinition> definitions, IOptions<FaultOptions> options, TimeProvider time)
    {
        _definitions = definitions.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
        _options = options.Value;
        _time = time;
    }

    public bool IsOn(string name)
    {
        lock (_gate)
        {
            return RemainingAt(name, _time.GetUtcNow()) is not null;
        }
    }

    public IReadOnlyList<FaultStatus> List()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return [.. _definitions.Values.OrderBy(definition => definition.Owner, StringComparer.Ordinal).ThenBy(definition => definition.Name, StringComparer.Ordinal).Select(definition => StatusAt(definition, now))];
        }
    }

    public SwitchOnResult SwitchOn(string name, TimeSpan? duration, out FaultStatus? status)
    {
        status = null;
        if (!_definitions.TryGetValue(name, out var definition))
        {
            return SwitchOnResult.UnknownFault;
        }

        var length = duration ?? _options.DefaultDuration;
        if (length <= TimeSpan.Zero || length > _options.MaxDuration)
        {
            return SwitchOnResult.DurationNotAllowed;
        }

        lock (_gate)
        {
            var now = _time.GetUtcNow();
            _until[name] = now + length;
            status = StatusAt(definition, now);
        }

        return SwitchOnResult.Switched;
    }

    public bool SwitchOff(string name, out FaultStatus? status)
    {
        status = null;
        if (!_definitions.TryGetValue(name, out var definition))
        {
            return false;
        }

        lock (_gate)
        {
            _until.Remove(name);
            status = StatusAt(definition, _time.GetUtcNow());
        }

        return true;
    }

    // Callers hold the gate. An expired entry is dropped when it is found, so nothing is left behind.
    private TimeSpan? RemainingAt(string name, DateTimeOffset now)
    {
        if (!_until.TryGetValue(name, out var until))
        {
            return null;
        }

        if (until <= now)
        {
            _until.Remove(name);
            return null;
        }

        return until - now;
    }

    private FaultStatus StatusAt(FaultDefinition definition, DateTimeOffset now) =>
        RemainingAt(definition.Name, now) is { } remaining
            ? new FaultStatus(definition.Name, definition.Owner, definition.Description, true, now + remaining, (int)Math.Ceiling(remaining.TotalSeconds))
            : new FaultStatus(definition.Name, definition.Owner, definition.Description, false, null, null);
}
