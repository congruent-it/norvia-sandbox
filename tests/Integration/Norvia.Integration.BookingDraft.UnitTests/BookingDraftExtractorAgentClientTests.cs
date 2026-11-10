using Norvia.Agents.Runner;

namespace Norvia.Integration.BookingDraft.UnitTests;

/// <summary>The extractor over the port (<see cref="IAgentClient"/>): the same checks whichever client answers, in process or Foundry.</summary>
public sealed class BookingDraftExtractorAgentClientTests
{
    private sealed class FakeAgentClient(Func<AgentQuestion, string> reply) : IAgentClient
    {
        public AgentQuestion? Asked { get; private set; }

        public Task<string> AskAsync(AgentQuestion question, CancellationToken cancellationToken)
        {
            Asked = question;
            return Task.FromResult(reply(question));
        }
    }

    private const string Empty = """{"booking": {}, "quotes": {}, "missing": [{"field": "booking", "reason": "No booking."}]}""";

    [Fact]
    public async Task The_question_carries_the_wrapped_text_the_correlation_ID_and_the_run_ID_of_the_outcome()
    {
        var client = new FakeAgentClient(_ => Empty);

        var outcome = await new BookingDraftExtractor(client).ExtractAsync("Please move 2 pallets", "corr-1", CancellationToken.None);

        client.Asked!.Text.Should().Contain("<shipper_text>").And.Contain("Please move 2 pallets");
        client.Asked.CorrelationId.Should().Be("corr-1");
        client.Asked.RunId.Should().Be(outcome.RunId);
    }

    [Fact]
    public async Task A_reply_that_is_not_json_is_unavailable()
    {
        var outcome = await new BookingDraftExtractor(new FakeAgentClient(_ => "sorry")).ExtractAsync("text", "corr-1", CancellationToken.None);

        outcome.FailureCode.Should().Be(BookingDraftFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task A_client_that_throws_is_unavailable()
    {
        var client = new FakeAgentClient(_ => throw new InvalidOperationException("down"));

        var outcome = await new BookingDraftExtractor(client).ExtractAsync("text", "corr-1", CancellationToken.None);

        outcome.FailureCode.Should().Be(BookingDraftFailureCodes.AgentUnavailable);
    }

    [Fact]
    public async Task A_refusal_for_more_than_one_booking_is_passed_on()
    {
        var client = new FakeAgentClient(_ => """{"refusal": "MultipleBookingsInText"}""");

        var outcome = await new BookingDraftExtractor(client).ExtractAsync("two bookings", "corr-1", CancellationToken.None);

        outcome.FailureCode.Should().Be(BookingDraftFailureCodes.MultipleBookingsInText);
    }

    [Fact]
    public async Task A_reply_in_a_code_fence_is_read_whichever_client_gave_it()
    {
        var client = new FakeAgentClient(_ => "```json\n" + Empty + "\n```");

        var outcome = await new BookingDraftExtractor(client).ExtractAsync("text", "corr-1", CancellationToken.None);

        outcome.Draft.Should().NotBeNull();
    }
}
