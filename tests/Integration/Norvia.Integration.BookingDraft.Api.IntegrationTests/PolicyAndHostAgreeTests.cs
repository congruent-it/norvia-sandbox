using System.Xml.Linq;

namespace Norvia.Integration.BookingDraft.Api.IntegrationTests;

/// <summary>
/// API Management's policy (infra/estate/partner-api/create-booking-draft.policy.xml) and the host are two halves of one operation, and only the
/// gateway runs the policy, so a smoke run after a deploy is the first place they would meet. These tests meet them earlier: what the policy sends is
/// what the host route, the host's headers, the host's role and the host's size limit expect. The policy's own checks (key, token, role, <c>azp</c>,
/// rate limit, size cap) are read in the Tms architecture tests' <c>BookingDraftPolicyTests</c>, as for the other operations.
/// </summary>
public sealed class PolicyAndHostAgreeTests
{
    private static readonly string Root = FindRoot();

    private static readonly XElement Policy = XElement.Parse(File.ReadAllText(Path.Combine(Root, "infra/estate/partner-api/create-booking-draft.policy.xml")));

    private static XElement Inbound => Policy.Element("inbound")!;

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Found no Norvia.slnx above the test assembly.");
    }

    [Fact]
    public void ThePolicy_RewritesToTheHostsRoute()
    {
        Inbound.Element("rewrite-uri")!.Attribute("template")!.Value.Should().Be(BookingDraftEndpoint.Route);
        AzureHost.Route.Should().Be(BookingDraftEndpoint.Route);
    }

    [Fact]
    public void ThePolicy_SetsExactlyTheHeadersTheHostReads()
    {
        var set = Inbound.Elements("set-header").Select(header => header.Attribute("name")!.Value).Where(name => name.StartsWith("X-", StringComparison.Ordinal));

        set.Should().BeEquivalentTo(BookingDraftEndpoint.CorrelationHeader, BookingDraftEndpoint.ShipperHeader);
    }

    [Fact]
    public void ThePolicy_TracesTheFlowTheHostTraces()
    {
        var flows = Policy.Descendants("trace").SelectMany(trace => trace.Elements("metadata")).Where(metadata => metadata.Attribute("name")!.Value == "flow").Select(metadata => metadata.Attribute("value")!.Value);

        flows.Should().NotBeEmpty().And.OnlyContain(flow => flow == BookingDraftEndpoint.Flow);
    }

    [Fact]
    public void TheHostsRole_IsTheOneTheHostsApiDefines_AndTheGatewayIsGivenNothingElse()
    {
        var module = File.ReadAllText(Path.Combine(Root, "infra/estate/booking-draft.bicep"));

        module.Should().Contain($"var role = '{GatewayAuthentication.Role}'");
        Policy.ToString().Should().NotContain(GatewayAuthentication.Role, "the role is the host's door, not the Shipper's: the policy asks the Shipper for TransportOrders.Draft");
    }

    [Fact]
    public void ThePolicysSizeCap_HoldsEveryCharacterTheHostAccepts()
    {
        // The policy caps bytes before the host caps characters; at 4 bytes a character plus the JSON around the text, the host's default must fit under the cap.
        var caps = System.Text.RegularExpressions.Regex.Matches(Inbound.ToString(), @"(?:>|&gt;) (\d+)").Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct();
        var cap = caps.Should().ContainSingle().Subject;
        var defaults = new BookingDraftHostOptions();

        cap.Should().Be(40_960);
        (defaults.MaxInputCharacters * 4 + 1_024).Should().BeLessThanOrEqualTo(cap);
    }
}
