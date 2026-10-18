using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests.BillingLogicApp;

/// <summary>
/// The sample messages in contracts/canonical-model/samples/transport-order-billable. The Billing Logic App sends the message body to
/// the ERP billing API as it is, so a message the workflow reads is the call it makes; CI runs these without Azure.
/// </summary>
public sealed class BillingSampleTests
{
    private const string Folder = "CanonicalModel/samples/transport-order-billable";

    public static TheoryData<string> Samples => new(Contracts.FileNames(Folder).Select(name => name[..^".json".Length]));

    private static string Message(string sample) => Contracts.Read($"{Folder}/{sample}.json");

    [Fact]
    public void Samples_CoverAFreightOnlyLine_AMinimumChargeAdjustmentAndAReleaseWithoutProofOfDelivery() =>
        Contracts.FileNames(Folder).Should().BeEquivalentTo("freight-only.json", "minimum-charge-adjustment.json", "released-without-proof-of-delivery.json");

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleMessage_KeepsToTheCanonicalSchema(string sample)
    {
        var results = Schemas.TransportOrderBillable.Validate(JsonNode.Parse(Message(sample)));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Tracking_SampleMessage_GivesTheKeysTheWorkflowTracks(string sample)
    {
        // What the Compose step reads from the base64 content data: the keys the tracking contract names, none empty.
        var contentData = Convert.ToBase64String(Encoding.UTF8.GetBytes(Message(sample)));
        var message = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(contentData)))!;
        var inputs = BillingWorkflow.Action(BillingWorkflow.Tracking)["inputs"]!.AsObject();

        foreach (var (key, expression) in inputs)
        {
            var field = expression!.GetValue<string>().Split("?['")[^1].TrimEnd(']', '\'');
            message[field].Should().NotBeNull($"the workflow tracks {key} from {field}");
            message[field]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }

        message["eventType"]!.GetValue<string>().Should().Be("TransportOrderBillable");
    }
}
