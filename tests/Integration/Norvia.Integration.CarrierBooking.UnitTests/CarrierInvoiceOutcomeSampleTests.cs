using System.Text;
using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

/// <summary>
/// The sample messages in contracts/canonical-model/samples/carrier-invoice-approved: the event, and the outcome the map makes of it. CI runs
/// them without Azure.
/// </summary>
public sealed class CarrierInvoiceOutcomeSampleTests
{
    private const string Folder = "CanonicalModel/samples/carrier-invoice-approved";

    public static TheoryData<string> Samples => new(Contracts.FileNames(Folder).Select(name => name[..^".json".Length]));

    private static string Message(string sample) => Contracts.Read($"{Folder}/{sample}.json");

    [Fact]
    public void Samples_CoverAnInvoiceThatMatchedInFullAndOneFinanceAdjustedAndRejectedLinesOf() =>
        Contracts.FileNames(Folder).Should().BeEquivalentTo("all-matched.json", "adjusted-and-rejected.json");

    [Theory]
    [MemberData(nameof(Samples))]
    public void Validate_SampleEvent_KeepsToTheCanonicalSchema(string sample)
    {
        var results = Schemas.CarrierInvoiceApproved.Validate(JsonNode.Parse(Message(sample)));

        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierInvoiceOutcome_SampleEvent_ValidatesAgainstTheCarrierInvoiceOutcomeSchema(string sample)
    {
        var outcome = CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(Message(sample));

        var results = Schemas.CarrierInvoiceOutcome.Validate(outcome);
        results.IsValid.Should().BeTrue(Schemas.Errors(results));
    }

    [Fact]
    public void ToCarrierInvoiceOutcome_TheAdjustedSample_GivesTheSampleOutcomeOfThePartnerApi()
    {
        // The Partner API's sample is the same invoice under a number of the Carrier's, with an accepted third line.
        var outcome = CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(Message("adjusted-and-rejected"))!;
        var sample = JsonNode.Parse(Contracts.Read("PartnerApi/samples/carrier-invoice-outcomes/with-rejection.carrier-invoice-outcome.json"))!;

        outcome["lines"]![3]!["result"]!.GetValue<string>().Should().Be(sample["lines"]![1]!["result"]!.GetValue<string>());
        outcome["lines"]![3]!["acceptedAmount"]!.GetValue<decimal>().Should().Be(sample["lines"]![1]!["acceptedAmount"]!.GetValue<decimal>());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierInvoiceOutcome_SampleEvent_KeepsTheAmountsTheErpGave(string sample)
    {
        var approved = JsonNode.Parse(Message(sample))!;

        var outcome = CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(Message(sample))!;

        outcome["approvedAmount"]!.GetValue<decimal>().Should().Be(approved["approvedAmount"]!.GetValue<decimal>());
        outcome["lines"]!.AsArray().Select(line => line!["acceptedAmount"]!.GetValue<decimal>())
            .Should().Equal(approved["lines"]!.AsArray().Select(line => line!["acceptedAmount"]?.GetValue<decimal>() ?? 0m));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierInvoiceOutcome_TheApprovedAmount_IsTheSumOfTheLinesAcceptedAmounts(string sample)
    {
        var outcome = CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(Message(sample))!;

        outcome["approvedAmount"]!.GetValue<decimal>().Should().Be(outcome["lines"]!.AsArray().Sum(line => line!["acceptedAmount"]!.GetValue<decimal>()));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToCarrierInvoiceOutcome_TheMessageAsTheTriggerGivesIt_IsTheSameOutcome(string sample)
    {
        // The Logic Apps trigger gives a message's content base64-encoded (ADR 0019).
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(Message(sample)));

        var fromEncoded = CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(MessageContent.Text(encoded));

        JsonNode.DeepEquals(fromEncoded, CarrierInvoiceOutcomeMap.ToCarrierInvoiceOutcome(Message(sample))).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Tracking_SampleMessage_GivesTheKeysTheWorkflowTracks(string sample)
    {
        var message = JsonNode.Parse(Message(sample))!;
        var inputs = CarrierInvoiceOutcomeLogicApp.CarrierInvoiceOutcomeWorkflow.Action(CarrierInvoiceOutcomeLogicApp.CarrierInvoiceOutcomeWorkflow.Tracking)["inputs"]!.AsObject();

        foreach (var (key, expression) in inputs)
        {
            ShipperInvoiceSampleTests.Resolve(message, expression!.GetValue<string>()).Should().NotBeNullOrWhiteSpace($"the workflow tracks {key}");
        }
    }
}
