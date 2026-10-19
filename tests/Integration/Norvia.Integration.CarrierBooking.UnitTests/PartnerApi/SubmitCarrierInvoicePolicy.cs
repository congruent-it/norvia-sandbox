using System.Xml.Linq;

namespace Norvia.Integration.CarrierBooking.UnitTests.PartnerApi;

/// <summary>
/// API Management's policy for the Norvia Partner API's "submit Carrier invoice" operation,
/// infra/estate/partner-api/submit-carrier-invoice.policy.xml.
/// </summary>
internal static class SubmitCarrierInvoicePolicy
{
    private static readonly Lazy<XElement> PolicyFile =
        new(() => XElement.Parse(Contracts.Read("PartnerApiPolicies/submit-carrier-invoice.policy.xml")));

    public static XElement Inbound => Section("inbound");

    public static XElement Outbound => Section("outbound");

    public static XElement OnError => Section("on-error");

    /// <summary>The inbound check whose condition matches, and the status it answers with when the condition holds.</summary>
    public static (string Condition, int Status) Refusal(Func<string, bool> condition)
    {
        var when = Inbound.Elements("choose").Elements("when")
            .SingleOrDefault(when => condition(when.Attribute("condition")!.Value))
            ?? throw new InvalidOperationException("The inbound section has no such check.");
        var status = when.Element("return-response")?.Element("set-status")?.Attribute("code")?.Value
            ?? throw new InvalidOperationException("The check doesn't answer with a status of its own.");
        return (when.Attribute("condition")!.Value, int.Parse(status, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>How the inbound section sets a header on the call to the Logic App.</summary>
    public static (string ExistsAction, string Value) Header(string name)
    {
        var header = Inbound.Elements("set-header").SingleOrDefault(header => header.Attribute("name")!.Value == name)
            ?? throw new InvalidOperationException($"The inbound section doesn't set {name}.");
        return (header.Attribute("exists-action")!.Value, header.Element("value")!.Value);
    }

    /// <summary>The expression the inbound section sets a variable to.</summary>
    public static string Variable(string name) =>
        Inbound.Elements("set-variable").SingleOrDefault(variable => variable.Attribute("name")!.Value == name)?.Attribute("value")!.Value
        ?? throw new InvalidOperationException($"The inbound section doesn't set the variable {name}.");

    /// <summary>The tracking contract's keys a trace records (coding guidelines §7), by name.</summary>
    public static IReadOnlyDictionary<string, string> TrackedKeys(XElement trace) =>
        trace.Elements("metadata").ToDictionary(metadata => metadata.Attribute("name")!.Value, metadata => metadata.Attribute("value")!.Value);

    private static XElement Section(string name) =>
        PolicyFile.Value.Element(name) ?? throw new InvalidOperationException($"The policy has no {name} section.");
}
