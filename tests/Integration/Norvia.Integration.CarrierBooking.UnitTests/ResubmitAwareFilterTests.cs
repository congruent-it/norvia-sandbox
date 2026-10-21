using System.Text.RegularExpressions;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>
/// A message resubmitted from the Integration console carries <c>resubmitTo</c>, and every subscription's filter takes it only when it
/// carries that subscription's own name (coding guidelines §7). A subscription added without the form fails here.
/// </summary>
public sealed partial class ResubmitAwareFilterTests
{
    [Fact]
    public void EverySubscription_OfTheEstate_HasAFilterWithTheResubmitToForm()
    {
        var messaging = Contracts.Read("Estate/messaging.bicep");
        var subscriptions = Subscriptions().Matches(messaging).ToDictionary(match => match.Groups["symbol"].Value, match => match.Groups["name"].Value);
        var filters = Filters().Matches(messaging).ToDictionary(match => match.Groups["parent"].Value, match => match.Groups["expression"].Value);

        subscriptions.Should().NotBeEmpty();
        foreach (var (symbol, name) in subscriptions)
        {
            filters.Should().ContainKey(symbol, $"the {name} subscription ({symbol}) needs a filter of its own");
            var expression = filters[symbol].Replace("\\'", "'", StringComparison.Ordinal);
            expression.Should().Contain("resubmitTo IS NULL", $"{symbol}'s filter takes an ordinary message only when it carries no resubmitTo")
                .And.EndWith($" OR resubmitTo = '{name}'", $"{symbol}'s filter takes a resubmit that names {name}");
        }
    }

    [GeneratedRegex(@"resource (?<symbol>\w+) 'Microsoft\.ServiceBus/namespaces/topics/subscriptions@[\d-]+' = \{\s*parent: \w+\s*name: '(?<name>[^']+)'")]
    private static partial Regex Subscriptions();

    [GeneratedRegex(@"resource \w+ 'Microsoft\.ServiceBus/namespaces/topics/subscriptions/rules@[\d-]+' = \{\s*parent: (?<parent>\w+)[\s\S]*?sqlExpression: '(?<expression>(?:[^'\\]|\\.)*)'")]
    private static partial Regex Filters();
}
