using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Runner;
using UglyToad.PdfPig.Writer;

namespace Norvia.Integration.BookingDraft.UnitTests;

/// <summary>What a host answers once the edge has let a request through: the same status, code and body locally and in Azure.</summary>
public sealed class BookingDraftOperationTests
{
    private sealed class FakeAgentClient(Func<string> reply, List<string>? questions = null) : IAgentClient
    {
        public Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken)
        {
            questions?.Add(question.Text);
            return Task.FromResult(reply());
        }
    }

    private const string Empty = """{"booking": {}, "quotes": {}, "missing": [{"field": "booking", "reason": "No booking."}]}""";

    private static MemoryStream Body(string json) => new(Encoding.UTF8.GetBytes(json));

    private static Task<BookingDraftAnswer> DraftAsync(string body, Func<string>? reply = null, int maxCharacters = 8_000) =>
        BookingDraftOperation.DraftAsync(new BookingDraftExtractor(new FakeAgentClient(reply ?? (() => Empty))), Body(body), "application/json",
            new BookingDraftLimits(maxCharacters, 1_000_000, 10), "corr-1", CancellationToken.None);

    private static Task<BookingDraftAnswer> DraftAsync(byte[] body, string contentType, Func<string>? reply = null, List<string>? questions = null, BookingDraftLimits? limits = null) =>
        BookingDraftOperation.DraftAsync(new BookingDraftExtractor(new FakeAgentClient(reply ?? (() => Empty), questions)), new MemoryStream(body), contentType,
            limits ?? new BookingDraftLimits(8_000, 1_000_000, 10), "corr-1", CancellationToken.None);

    /// <summary>A PDF with one page per line of text group; no lines at all makes pages with nothing to read, as a scan has.</summary>
    private static byte[] Pdf(params string[] pages)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        foreach (var text in pages)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            if (text.Length > 0)
            {
                page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(25, 700), font);
            }
        }

        return builder.Build();
    }

    [Fact]
    public async Task A_text_PDF_gives_the_agent_its_text_and_a_draft_with_200()
    {
        var questions = new List<string>();
        var answer = await DraftAsync(Pdf("Please move two pallets"), "application/pdf", () => Empty, questions);

        answer.StatusCode.Should().Be(200);
        answer.Outcome.Should().Be(BookingDraftOperation.Succeeded);
        questions.Should().ContainSingle().Which.Should().Contain("Please move two pallets");
    }

    [Fact]
    public async Task A_scanned_PDF_with_no_text_is_422_NoTextInPdf_and_never_reaches_the_agent()
    {
        var questions = new List<string>();

        var answer = await DraftAsync(Pdf("", ""), "application/pdf", questions: questions);

        answer.StatusCode.Should().Be(422);
        answer.FailureCode.Should().Be(BookingDraftOperation.NoTextInPdf);
        answer.Outcome.Should().Be(BookingDraftOperation.Refused);
        answer.Body["text"]!.GetValue<string>().Should().Be("This PDF has no text to read; paste the text instead.");
        questions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/xml")]
    [InlineData("application/msword")]
    public async Task A_format_that_is_not_text_or_PDF_is_415_UnsupportedFormat(string contentType)
    {
        var answer = await DraftAsync(Encoding.UTF8.GetBytes("whatever"), contentType);

        answer.StatusCode.Should().Be(415);
        answer.FailureCode.Should().Be(BookingDraftOperation.UnsupportedFormat);
        answer.Outcome.Should().Be(BookingDraftOperation.Refused);
    }

    [Fact]
    public async Task Plain_text_is_read_as_the_text()
    {
        var questions = new List<string>();

        var answer = await DraftAsync(Encoding.UTF8.GetBytes("Please move two pallets"), "text/plain; charset=utf-8", questions: questions);

        answer.StatusCode.Should().Be(200);
        questions.Should().ContainSingle().Which.Should().Contain("Please move two pallets");
    }

    [Fact]
    public async Task A_file_that_is_not_a_readable_PDF_is_415_UnsupportedFormat()
    {
        var answer = await DraftAsync(Encoding.UTF8.GetBytes("%PDF-nonsense"), "application/pdf");

        answer.StatusCode.Should().Be(415);
        answer.FailureCode.Should().Be(BookingDraftOperation.UnsupportedFormat);
    }

    [Fact]
    public async Task A_PDF_over_the_byte_cap_is_413_InputTooLarge_before_it_is_read()
    {
        var pdf = Pdf("Please move two pallets");

        var answer = await DraftAsync(pdf, "application/pdf", limits: new BookingDraftLimits(8_000, pdf.Length - 1, 10));

        answer.StatusCode.Should().Be(413);
        answer.FailureCode.Should().Be(BookingDraftOperation.InputTooLarge);
    }

    [Fact]
    public async Task A_PDF_over_the_page_cap_is_413_InputTooLarge()
    {
        var questions = new List<string>();

        var answer = await DraftAsync(Pdf("one", "two", "three"), "application/pdf", questions: questions, limits: new BookingDraftLimits(8_000, 1_000_000, 2));

        answer.StatusCode.Should().Be(413);
        answer.FailureCode.Should().Be(BookingDraftOperation.InputTooLarge);
        answer.Body["text"]!.GetValue<string>().Should().Be("The PDF is over 2 pages or 976 KB.");
        questions.Should().BeEmpty();
    }

    [Fact]
    public async Task A_PDF_with_more_text_than_the_character_limit_is_413_as_pasted_text_is()
    {
        var answer = await DraftAsync(Pdf(new string('x', 101)), "application/pdf", limits: new BookingDraftLimits(100, 1_000_000, 10));

        answer.StatusCode.Should().Be(413);
        answer.FailureCode.Should().Be(BookingDraftOperation.InputTooLarge);
    }

    [Fact]
    public async Task A_text_gives_the_draft_with_200()
    {
        var answer = await DraftAsync("""{"text": "Please move two pallets"}""");

        answer.StatusCode.Should().Be(200);
        answer.Outcome.Should().Be(BookingDraftOperation.Succeeded);
        answer.FailureCode.Should().BeNull();
        answer.Body.ContainsKey("booking").Should().BeTrue();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"other": 1}""")]
    [InlineData("""{"text": 5}""")]
    [InlineData("""{"text": "   "}""")]
    public async Task A_request_without_a_text_is_400(string body)
    {
        var answer = await DraftAsync(body);

        answer.StatusCode.Should().Be(400);
        answer.FailureCode.Should().Be(BookingDraftOperation.TextRequired);
        answer.Outcome.Should().Be(BookingDraftOperation.Refused);
        answer.Body["messageId"]!.GetValue<string>().Should().Be("corr-1");
        answer.Body["failureCode"]!.GetValue<string>().Should().Be("TextRequired");
    }

    [Fact]
    public async Task A_text_over_the_limit_is_413_and_never_reaches_the_agent()
    {
        var asked = false;

        var answer = await DraftAsync($$"""{"text": "{{new string('x', 101)}}"}""", () => { asked = true; return Empty; }, maxCharacters: 100);

        answer.StatusCode.Should().Be(413);
        answer.FailureCode.Should().Be(BookingDraftOperation.InputTooLarge);
        answer.Body["text"]!.GetValue<string>().Should().Be("The text is longer than 100 characters.");
        asked.Should().BeFalse();
    }

    [Fact]
    public async Task Text_for_more_than_one_booking_is_422()
    {
        var answer = await DraftAsync("""{"text": "two bookings"}""", () => """{"refusal": "MultipleBookingsInText"}""");

        answer.StatusCode.Should().Be(422);
        answer.FailureCode.Should().Be(BookingDraftFailureCodes.MultipleBookingsInText);
        answer.Outcome.Should().Be(BookingDraftOperation.Refused);
    }

    [Fact]
    public async Task An_agent_that_fails_is_503_and_a_failed_outcome()
    {
        var answer = await DraftAsync("""{"text": "move"}""", () => throw new InvalidOperationException("down"));

        answer.StatusCode.Should().Be(503);
        answer.FailureCode.Should().Be(BookingDraftFailureCodes.AgentUnavailable);
        answer.Outcome.Should().Be(BookingDraftOperation.Failed);
        answer.Body["text"]!.GetValue<string>().Should().Be("The assistant is unavailable.");
    }

    [Fact]
    public void A_refusal_names_the_correlation_ID_the_code_and_the_text()
    {
        var answer = BookingDraftOperation.Refuse("corr-9", 503, BookingDraftOperation.AgentsDisabled, "The assistant is unavailable.");

        answer.Body.ToJsonString().Should().Be(new JsonObject { ["messageId"] = "corr-9", ["failureCode"] = "AgentsDisabled", ["text"] = "The assistant is unavailable." }.ToJsonString());
    }
}
