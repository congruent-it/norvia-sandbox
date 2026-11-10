using System.Text;
using System.Text.Json.Nodes;
using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft.UnitTests;

/// <summary>What a host answers once the edge has let a request through: the same status, code and body locally and in Azure.</summary>
public sealed class BookingDraftOperationTests
{
    private sealed class FakeAgentClient(Func<string> reply) : IAgentClient
    {
        public Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken) => Task.FromResult(reply());
    }

    private const string Empty = """{"booking": {}, "quotes": {}, "missing": [{"field": "booking", "reason": "No booking."}]}""";

    private static MemoryStream Body(string json) => new(Encoding.UTF8.GetBytes(json));

    private static Task<BookingDraftAnswer> DraftAsync(string body, Func<string>? reply = null, int maxCharacters = 8_000) =>
        BookingDraftOperation.DraftAsync(new BookingDraftExtractor(new FakeAgentClient(reply ?? (() => Empty))), Body(body), maxCharacters, "corr-1", CancellationToken.None);

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
