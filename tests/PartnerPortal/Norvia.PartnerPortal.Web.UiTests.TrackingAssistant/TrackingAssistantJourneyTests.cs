using Microsoft.Extensions.AI;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Norvia.PartnerPortal.Web.UiTests.TrackingAssistant;

/// <summary>
/// The Shipper tracking assistant's journeys (P8, ADR 0049, #883): a Shipper Persona asks about an order on its tracking page, and the portal answers from the scripted fake model with what the
/// Partner API returned for that Persona. Each test enters its own Transport Order and starts a portal of its own, so none depends on another's data or counts.
/// </summary>
public sealed class TrackingAssistantJourneyTests(TrackingAssistantUiFixture norvia) : BrowserPage(norvia)
{
    [Fact]
    public async Task WhereIsMyOrder_OnTheOrdersPage_ShowsAnAnswerThatNamesItsTrackingStatusAndEta()
    {
        await using var host = Norvia.StartPortal();
        var number = await OpenOrderAsync(host.Address);

        await AskAsync("Where is my order?");

        await Expect(Answers()).ToHaveCountAsync(1);
        await Expect(Answers().First).ToContainTextAsync(number);
        await Expect(Answers().First).ToContainTextAsync("Received");
        await Expect(Answers().First).ToContainTextAsync("The ETA is an estimate");
    }

    [Fact]
    public async Task TheSameQuestionInDutch_GetsADutchAnswer()
    {
        await using var host = Norvia.StartPortal();
        var number = await OpenOrderAsync(host.Address);

        await AskAsync("Waar is mijn bestelling?");

        await Expect(Answers()).ToHaveCountAsync(1);
        await Expect(Answers().First).ToContainTextAsync(number);
        await Expect(Answers().First).ToContainTextAsync("Ontvangen");
        await Expect(Answers().First).ToContainTextAsync("De ETA is een schatting");
    }

    [Fact]
    public async Task AFollowUpQuestion_WorksInTheSamePanel_AndAReloadStartsANewConversation()
    {
        await using var host = Norvia.StartPortal();
        await OpenOrderAsync(host.Address);
        await AskAsync("Where is my order?");
        await Expect(Answers()).ToHaveCountAsync(1);

        await AskAsync("And when will it be delivered?");

        await Expect(Answers()).ToHaveCountAsync(2);
        await Expect(Conversation()).ToContainTextAsync("And when will it be delivered?");
        await Expect(Portal.GetByLabel("Your question", new() { Exact = true })).ToHaveValueAsync(string.Empty);

        await Portal.ReloadAsync();

        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = "Ask about your orders", Level = 2 })).ToBeVisibleAsync();
        await Expect(Conversation().GetByRole(AriaRole.Listitem)).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("What is the price of this order?")]
    [InlineData("Why is my order late?")]
    public async Task AQuestionTheDataCannotAnswer_ShowsThatTheAssistantHasNoInformation(string question)
    {
        await using var host = Norvia.StartPortal();
        await OpenOrderAsync(host.Address);

        await AskAsync(question);

        await Expect(Conversation()).ToContainTextAsync("I have no information about that");
        await Expect(Conversation()).Not.ToContainTextAsync("Received");
    }

    [Fact]
    public async Task OverTheQuestionLimit_ThePanelSaysToTryAgainLater_AndTheTrackingScreenStillWorks()
    {
        await using var host = Norvia.StartPortal(new Dictionary<string, string> { ["Agents:Limits:QuestionsPerMinute"] = "1" });
        var number = await OpenOrderAsync(host.Address);
        await AskAsync("Where is my order?");
        await Expect(Answers()).ToHaveCountAsync(1);

        await AskAsync("Where is my order now?");

        await Expect(Notices()).ToHaveCountAsync(1);
        await Expect(Notices().First).ToContainTextAsync("Try again in a few minutes");
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByLabel("Transport Order", new() { Exact = true })).ToContainTextAsync("Received");
    }

    [Fact]
    public async Task WhenTheAssistantIsUnavailable_ThePanelSaysSo_AndTheTrackingScreenStillWorks()
    {
        await using var host = Norvia.StartPortal(model: new FailingChatClient());
        var number = await OpenOrderAsync(host.Address);

        await AskAsync("Where is my order?");

        await Expect(Notices()).ToHaveCountAsync(1);
        await Expect(Notices().First).ToContainTextAsync("Assistant unavailable");
        await Expect(Portal.GetByRole(AriaRole.Heading, new() { Name = number, Level = 1 })).ToBeVisibleAsync();
        await Expect(Portal.GetByLabel("Transport Order", new() { Exact = true })).ToContainTextAsync("Received");
    }

    /// <summary>A model that is out of reach, as when Foundry is down.</summary>
    private sealed class FailingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("The model is out of reach.");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("The model is out of reach.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
