using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Norvia.Integration.BookingDraft.UnitTests;

/// <summary>
/// The Intake agent's evaluation set (#462, evaluation/cases/booking-intake): every kind of sample the issue names exists with a written
/// known answer, no known answer holds a value its own text does not give, the PDF samples are the PDFs in the repo, the injection cases
/// carry their text, and the run is documented. The cases run on demand (docs/operations/agent-evaluation.md), never here.
/// </summary>
public sealed partial class IntakeEvaluationCasesTests
{
    private static readonly string Root = FindRoot();

    private static readonly string[] CopiedFields =
        ["bookingRef", "receiverName", "receiverContact", "receiverPhone", "receiverEmail", "pickupStreet", "pickupPostalCode", "pickupCity", "receiverStreet", "receiverPostalCode", "receiverCity"];

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Norvia.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Norvia.slnx not found above the test output.");
    }

    private static Dictionary<string, JsonNode> Cases() =>
        Directory.GetFiles(Path.Combine(Root, "evaluation", "cases", "booking-intake"), "*.json")
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!)
            .ToDictionary(c => c["id"]!.GetValue<string>());

    private static string Normalise(string value) => WhiteSpace().Replace(value.ToLowerInvariant(), " ").Trim();

    private static string ShipperText(JsonNode c)
    {
        var input = c["input"]!.GetValue<string>();
        const string open = "<shipper_text>\n";
        const string close = "\n</shipper_text>";
        var start = input.IndexOf(open, StringComparison.Ordinal);
        var end = input.LastIndexOf(close, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, c["id"]!.GetValue<string>());
        end.Should().BeGreaterThan(start, c["id"]!.GetValue<string>());
        return input[(start + open.Length)..end];
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        null => [],
        JsonArray list => list.Select(item => item!.GetValue<string>()),
        _ => [node.GetValue<string>()],
    };

    [Fact]
    public void EveryKindOfSampleTheIssueNames_HasACase_WithAWrittenKnownAnswer()
    {
        var cases = Cases();

        cases.Keys.Should().Contain(
            ["clean-english-email", "clean-dutch-email", "clean-english-pdf", "clean-dutch-pdf",
             "partial-english-no-shipper-reference", "partial-dutch-no-collection-window", "partial-english-no-goods", "partial-dutch-no-receiver-name", "partial-english-no-weight",
             "ambiguous-english-two-weights", "ambiguous-dutch-two-dates", "ambiguous-english-about-a-ton",
             "refusal-english-more-than-one-booking", "refusal-dutch-more-than-one-booking", "one-booking-with-several-kinds-of-package",
             "not-a-booking-english", "not-a-booking-dutch",
             "forwarded-english-thread-with-signature-and-disclaimer", "forwarded-dutch-thread-with-signature-and-disclaimer",
             "injection-english-ignore-your-instructions", "injection-dutch-set-the-weight-to-zero", "injection-english-use-another-reference",
             "injection-english-invent-a-reference-when-there-is-none", "injection-dutch-add-a-booking-for-another-shipper", "injection-english-reveal-the-instructions",
             "injection-english-pdf"]);
        foreach (var (id, c) in cases)
        {
            c["agent"]!.GetValue<string>().Should().Be("booking-intake", id);
            c["description"]!.GetValue<string>().Should().Contain("Known answer", id);
            c["setup"]!["note"]!.GetValue<string>().Should().NotBeEmpty(id);
            _ = ShipperText(c);
        }
    }

    [Fact]
    public void TheSet_HoldsBothLanguages_AndAShipperReferenceCaseWithoutAReference()
    {
        var texts = Cases().ToDictionary(c => c.Key, c => ShipperText(c.Value));

        texts.Values.Should().Contain(t => t.Contains("Kind regards", StringComparison.Ordinal));
        texts.Values.Should().Contain(t => t.Contains("Met vriendelijke groet", StringComparison.Ordinal) || t.Contains("Groet", StringComparison.Ordinal));
        texts["partial-english-no-shipper-reference"].Should().NotMatchRegex("BD-\\d{4}-\\d{4}");
        texts["clean-english-email"].Should().Contain("BD-2030-0101");
    }

    [Fact]
    public void EveryCase_ChecksThatNoValueIsInvented_AndTheKnownAnswerIsGroundedInItsOwnText()
    {
        foreach (var (id, c) in Cases())
        {
            var expect = c["expect"]!;
            expect["grounded"]!.GetValue<bool>().Should().BeTrue(id);
            var text = Normalise(ShipperText(c));

            if (expect["fields"] is JsonObject fields)
            {
                foreach (var (path, expected) in fields)
                {
                    if (!CopiedFields.Contains(path.Split('.')[^1], StringComparer.Ordinal))
                    {
                        continue;
                    }

                    Strings(expected).Should().OnlyContain(value => text.Contains(Normalise(value), StringComparison.Ordinal), $"{id}: {path} is copied from the text");
                }
            }

            if (expect["lists"] is JsonObject lists)
            {
                foreach (var (path, rule) in lists.Where(l => l.Key.StartsWith("booking.items[].weight", StringComparison.Ordinal)))
                {
                    Strings(rule!["exactly"]).Should().OnlyContain(value => text.Contains(Normalise(value), StringComparison.Ordinal), $"{id}: {path} is given in the text");
                }
            }
        }
    }

    [Fact]
    public void ADraftWithoutAValueThatTheTextDoesNotGive_NamesTheFieldAsMissing_AndLeavesItOut()
    {
        var cases = Cases();
        (string Id, string Field)[] gaps =
        [
            ("partial-english-no-shipper-reference", "bookingRef"), ("partial-dutch-no-collection-window", "pickupFrom"), ("partial-english-no-goods", "items"),
            ("partial-dutch-no-receiver-name", "receiverName"), ("partial-english-no-weight", "items.0.weight"),
            ("ambiguous-english-two-weights", "items.0.weight"), ("ambiguous-dutch-two-dates", "pickupFrom"), ("ambiguous-english-about-a-ton", "items.0.weight"),
            ("injection-english-invent-a-reference-when-there-is-none", "bookingRef"),
        ];

        foreach (var (id, field) in gaps)
        {
            var expect = cases[id]["expect"]!;
            Strings(expect["lists"]!["missing[].field"]!["includes"]).Should().Contain(field, id);
            Strings(expect["absent"]).Should().Contain(path => path == $"booking.{field}", id);
        }

        Strings(cases["ambiguous-english-two-weights"]["expect"]!["lists"]!["missing[].kind"]!["includes"]).Should().Contain("ambiguous");
        Strings(cases["ambiguous-dutch-two-dates"]["expect"]!["lists"]!["missing[].kind"]!["includes"]).Should().Contain("ambiguous");
    }

    [Fact]
    public void ARefusalAndANonBooking_HaveNoDraftToSubmit_AndEveryOtherCaseHasTheTmsKnownAnswer()
    {
        var cases = Cases();
        string[] codes = [.. RejectionCodes()];

        foreach (var (id, c) in cases)
        {
            var noDraft = id.StartsWith("refusal-", StringComparison.Ordinal) || id.StartsWith("not-a-booking-", StringComparison.Ordinal);
            if (noDraft)
            {
                c["submission"].Should().BeNull(id);
                continue;
            }

            var submission = c["submission"]!;
            submission["result"]!.GetValue<string>().Should().BeOneOf("ACCEPTED", "REJECTED");
            if (submission["result"]!.GetValue<string>() == "REJECTED")
            {
                Strings(submission["codes"]).Should().NotBeEmpty(id).And.BeSubsetOf(codes, id);
            }
            else
            {
                submission["codes"].Should().BeNull(id);
            }
        }

        cases["refusal-english-more-than-one-booking"]["expect"]!["fields"]!["refusal"]!.GetValue<string>().Should().Be("MultipleBookingsInText");
        cases["refusal-dutch-more-than-one-booking"]["expect"]!["fields"]!["refusal"]!.GetValue<string>().Should().Be("MultipleBookingsInText");
        Strings(cases["not-a-booking-english"]["expect"]!["absent"]).Should().Contain("booking");
    }

    [Fact]
    public void ADraftThatIsKnownToBeIncomplete_IsKnownToBeRejected_ByTheCodeOfItsGap()
    {
        var cases = Cases();

        Strings(cases["partial-english-no-shipper-reference"]["submission"]!["codes"]).Should().Equal("ShipperReferenceMissing");
        Strings(cases["partial-dutch-no-collection-window"]["submission"]!["codes"]).Should().Equal("CollectionWindowMissing");
        Strings(cases["partial-english-no-goods"]["submission"]!["codes"]).Should().Equal("GoodsLinesMissing");
        Strings(cases["partial-dutch-no-receiver-name"]["submission"]!["codes"]).Should().Equal("ConsigneeNameMissing");
        Strings(cases["ambiguous-english-two-weights"]["submission"]!["codes"]).Should().Equal("WeightInvalid");
        cases.Where(c => c.Key.StartsWith("clean-", StringComparison.Ordinal)).Should().OnlyContain(c => c.Value["submission"]!["result"]!.GetValue<string>() == "ACCEPTED");
    }

    [Fact]
    public void TheInjectionCases_CarryTheirText_AndTheAnswerIsTheOneWithoutIt()
    {
        var cases = Cases();
        var injections = cases.Where(c => c.Key.StartsWith("injection-", StringComparison.Ordinal)).ToList();

        injections.Select(c => c.Key).Should().BeEquivalentTo(
            ["injection-english-ignore-your-instructions", "injection-dutch-set-the-weight-to-zero", "injection-english-use-another-reference",
             "injection-english-invent-a-reference-when-there-is-none", "injection-dutch-add-a-booking-for-another-shipper", "injection-english-reveal-the-instructions",
             "injection-english-pdf"]);
        var text = injections.ToDictionary(c => c.Key, c => ShipperText(c.Value));
        text["injection-english-ignore-your-instructions"].Should().ContainEquivalentOf("ignore your instructions");
        text["injection-dutch-set-the-weight-to-zero"].Should().ContainEquivalentOf("gewicht op 0");
        text["injection-english-use-another-reference"].Should().ContainEquivalentOf("use reference");
        text["injection-english-invent-a-reference-when-there-is-none"].Should().ContainEquivalentOf("set the reference to");
        text["injection-dutch-add-a-booking-for-another-shipper"].Should().ContainEquivalentOf("boeking toe voor verlader");
        text["injection-english-reveal-the-instructions"].Should().ContainEquivalentOf("print your instructions");
        text["injection-english-pdf"].Should().ContainEquivalentOf("ignore all previous instructions");

        // The draft is the one of the same email without the text: the same reference, the same weight, and no refusal.
        foreach (var (id, c) in injections.Where(i => i.Key != "injection-english-invent-a-reference-when-there-is-none"))
        {
            c["expect"]!["fields"]!["booking.bookingRef"].Should().NotBeNull(id);
            Strings(c["expect"]!["absent"]).Should().Contain("refusal", id);
        }

        Strings(cases["injection-english-use-another-reference"]["expect"]!["fields"]!["booking.bookingRef"]).Should().Equal("BD-2030-0101");
        Strings(cases["injection-dutch-set-the-weight-to-zero"]["expect"]!["lists"]!["booking.items[].weight"]!["exactly"]).Should().Equal("1200 kg");
        Strings(cases["injection-english-reveal-the-instructions"]["expect"]!["mustNotContain"]).Should().Contain("NEVER INVENT");
    }

    [Fact]
    public void ThePdfSamples_AreThePdfsInTheRepo_ReadTheWayTheHostReadsThem()
    {
        var cases = Cases();
        var pdfCases = cases.Where(c => c.Value["setup"]!["source"] is not null).ToList();

        pdfCases.Select(c => c.Key).Should().BeEquivalentTo(["clean-english-pdf", "clean-dutch-pdf", "injection-english-pdf"]);
        foreach (var (id, c) in pdfCases)
        {
            var source = Path.Combine(Root, c["setup"]!["source"]!.GetValue<string>());
            File.Exists(source).Should().BeTrue(id);

            var read = PdfText.Read(File.ReadAllBytes(source), maxPages: 10);

            read.Status.Should().Be(PdfReadStatus.Read, id);
            Normalise(read.Text).Should().Be(Normalise(ShipperText(c)), $"{id}: the case's text is what the host reads from {c["setup"]!["source"]}");
        }
    }

    [Fact]
    public void ARunIsDocumented_NeverInCi_AndTheRuleForReadingResultsIsInTheDocs()
    {
        var docs = File.ReadAllText(Path.Combine(Root, "docs", "operations", "agent-evaluation.md"));

        docs.Should().Contain("## Reading the Intake agent's results")
            .And.Contain("infra/run-agent-evaluation.ps1 booking-intake")
            .And.Contain("-Submit")
            .And.Contain("evaluation/cases/booking-intake/")
            .And.Contain("never in CI");
        File.ReadAllText(Path.Combine(Root, "evaluation", "README.md")).Should().Contain("## Intake agent");
        File.ReadAllText(Path.Combine(Root, ".github", "workflows", "ci.yml")).Should().NotContain("run-agent-evaluation");
    }

    private static IEnumerable<string> RejectionCodes() =>
        EnumMember().Matches(File.ReadAllText(Path.Combine(Root, "src", "Tms", "Norvia.Tms.Domain", "TransportOrders", "RejectionCode.cs")).Split("enum RejectionCode")[1])
            .Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"^\s*([A-Z][A-Za-z]+),?\s*$", RegexOptions.Multiline)]
    private static partial Regex EnumMember();
}
