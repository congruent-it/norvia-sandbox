using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Carriers;
using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Web.IntegrationTests;

/// <summary>
/// Pool generation in Azure (#485) at the Simulator's boundary: the Simulator asks the Foundry project as it would in Azure (the Foundry client behind
/// <see cref="IAgentClient"/>, with a project address set), but the Responses endpoint is a fake and the credential is a stand-in, so there is no model
/// and no Azure. The Partner API and the TMS are the real ones.
/// </summary>
[Collection("Simulator runs")]
public sealed partial class FoundryPersonaPoolTests(SimulatorFixture fixture)
{
    private const string PricedShipper = "SHP-0001";
    private const string Project = "https://ais-norvia.services.ai.azure.com/api/projects/norvia";

    private static readonly CarrierShares Declines = new(
        ReleasePercent: 100, ConfirmPercent: 0, DeclinePercent: 100, TrackedPercent: 0, ExceptionPercent: 0,
        DeliveredPercent: 0, ProofOfDeliveryPercent: 0, CancelPercent: 0);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^Write (?<count>\d+) texts\.$", RegexOptions.Multiline)]
    private static partial Regex CountLine();

    [GeneratedRegex(@"^category: (?<category>.+)$", RegexOptions.Multiline)]
    private static partial Regex CategoryLine();

    [GeneratedRegex(@"^profile: (?<profile>.+)$", RegexOptions.Multiline)]
    private static partial Regex ProfileLine();

    [Fact]
    public async Task APlan_AsksTheFoundryProject_AsTheSimulatorsIdentity_AndKeepsTheAnswersAsPoolsWithTheirSource()
    {
        var foundry = new FakeFoundry();
        await using var simulator = await StartAsync(foundry);

        var preview = (await simulator.Requests.PreviewAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct)).Value;

        preview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => pool.Source.StartsWith("agent, run ", StringComparison.Ordinal));
        preview.PoolNotes.Should().BeEmpty();
        foundry.Requests.Should().NotBeEmpty().And.OnlyContain(request => request.Uri == $"{Project}/openai/v1/responses");
        foundry.Requests.Should().OnlyContain(request => request.Authorization == "Bearer foundry-token");
        foundry.Scopes.Should().OnlyContain(scope => scope == "https://ai.azure.com/.default");
        foreach (var request in foundry.Requests)
        {
            var body = JsonNode.Parse(request.Body)!;
            body["agent_reference"]!["name"]!.GetValue<string>().Should().Be("simulator-personas");
            body["store"]!.GetValue<bool>().Should().BeFalse("the service keeps nothing");
            body["max_output_tokens"]!.GetValue<int>().Should().Be(800);
        }

        // What the endpoint answered is what the plan holds, one pool for each question.
        preview.Pools.Should().HaveCount(foundry.Requests.Count);
        preview.Pools.Should().OnlyContain(pool => pool.Count == 8);
        preview.Pools.SelectMany(pool => pool.Samples).Should().OnlyContain(text => text.StartsWith("Fake ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APlan_PutsNoPlanDataInThePrompt_AndNoTextInTheLogOrTheTelemetry()
    {
        var foundry = new FakeFoundry();
        await using var simulator = await StartAsync(foundry);

        var preview = (await simulator.Requests.PreviewAsync(Runs.Carried(orders: 12, Runs.NextSeed(), Declines), Ct)).Value;

        foundry.Requests.Should().NotBeEmpty();
        foreach (var request in foundry.Requests)
        {
            var prompt = JsonNode.Parse(request.Body)!["input"]![0]!["content"]!.GetValue<string>();
            var lines = prompt.Split('\n');
            lines.Should().HaveCount(5, "the prompt is the count, the profile, the style, the language and the category, nothing of the plan");
            lines.Skip(1).Select(line => line.Split(':')[0]).Should().Equal("profile", "style", "language", "category");
        }

        var texts = preview.Pools.SelectMany(pool => pool.Samples).Distinct().ToList();
        texts.Should().NotBeEmpty();
        foreach (var text in texts)
        {
            simulator.Telemetry.Entries.Should().NotContain(entry => entry.Contains(text, StringComparison.Ordinal), "the log and the spans name a run and a profile, never a text");
        }

        simulator.Telemetry.Entries.Should().Contain(entry => entry.StartsWith("Agent run ", StringComparison.Ordinal) && entry.Contains("agent.run.id=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheGenerationCap_LimitsTheCallsOfAPlan_AndTheRestUseTheBuiltInPool()
    {
        var foundry = new FakeFoundry();
        await using var simulator = await StartAsync(foundry, ("Agents:MaxGenerationsPerPlan", "2"));

        var preview = (await simulator.Requests.PreviewAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct)).Value;

        foundry.Requests.Should().HaveCount(2, "no more generations than the cap");
        preview.Pools.Count(pool => pool.Source.StartsWith("agent, run ", StringComparison.Ordinal)).Should().Be(2);
        preview.Pools.Where(pool => !pool.Source.StartsWith("agent, run ", StringComparison.Ordinal)).Should().OnlyContain(pool => pool.Source == TextPool.BuiltInSource);
        preview.PoolNotes.Should().Contain(note => note.Contains("cap of 2 generations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheCostCap_LimitsTheCallsToWhatFitsInThePlansOutputTokens()
    {
        var foundry = new FakeFoundry();
        // Each generation may write 800 tokens (the agent definition's cap), so 1,700 pays for two, however many the generation cap allows.
        await using var simulator = await StartAsync(foundry, ("Agents:MaxGenerationsPerPlan", "100"), ("Agents:MaxOutputTokensPerPlan", "1700"));

        var preview = (await simulator.Requests.PreviewAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct)).Value;

        foundry.Requests.Should().HaveCount(2);
        preview.Pools.Where(pool => pool.Source == TextPool.BuiltInSource).Should().NotBeEmpty("the pools past the cost cap are the built-in ones");
        preview.PoolNotes.Should().Contain(note => note.Contains("cap of 2 generations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheGenerationCap_IsEachPlansOwn_SoTheNextPlanMayAskAgain()
    {
        var foundry = new FakeFoundry();
        await using var simulator = await StartAsync(foundry, ("Agents:MaxGenerationsPerPlan", "1"));
        var settings = Runs.Carried(orders: 9, Runs.NextSeed(), Declines);

        await simulator.Requests.PreviewAsync(settings, Ct);
        await simulator.Requests.PreviewAsync(settings, Ct);

        foundry.Requests.Should().HaveCount(2, "the cap is the plan's: each plan may ask once");
    }

    [Fact]
    public async Task AFoundryThatIsTooSlow_IsGivenUpOn_WithNoRetry_AndThePlanFallsBackToTheBuiltInPool()
    {
        var foundry = new FakeFoundry { Hang = true };
        await using var simulator = await StartAsync(foundry, ("Agents:TimeoutSeconds", "1"), ("Agents:MaxGenerationsPerPlan", "2"));

        var preview = (await simulator.Requests.PreviewAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct)).Value;

        preview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => pool.Source == TextPool.BuiltInSource);
        preview.PoolNotes.Should().Contain(note => note.Contains("built-in pool is used", StringComparison.Ordinal));
        foundry.Requests.Should().HaveCount(2, "each generation is asked once, with no retry");
    }

    [Fact]
    public async Task AFoundryThatRefuses_FallsBackToTheBuiltInPool_AndTheRunStillStartsAndCompletes()
    {
        var foundry = new FakeFoundry { Failure = HttpStatusCode.TooManyRequests };
        await using var simulator = await StartAsync(foundry);

        var (preview, run, _) = await simulator.RunToEndAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct);

        run.Status.Should().Be(Norvia.Simulator.Domain.Runs.SimulationRunStatus.Completed);
        preview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => pool.Source == TextPool.BuiltInSource);
        simulator.Telemetry.Entries.Should().Contain(entry => entry.Contains("The personas agent gave no pool for", StringComparison.Ordinal));
        simulator.Telemetry.Entries.Should().Contain(entry => entry.StartsWith("Agent run ", StringComparison.Ordinal) && entry.Contains("agent.name=simulator-personas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAnswerThatIsNotAListOfTexts_FallsBackToTheBuiltInPool()
    {
        var foundry = new FakeFoundry { Text = _ => "Sorry, I can't help with that." };
        await using var simulator = await StartAsync(foundry);

        var preview = (await simulator.Requests.PreviewAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct)).Value;

        preview.Pools.Should().NotBeEmpty().And.OnlyContain(pool => pool.Source == TextPool.BuiltInSource);
    }

    [Fact]
    public async Task ADeclineSentByARun_CarriesATextTheFoundryAgentWrote()
    {
        var foundry = new FakeFoundry();
        await using var simulator = await StartAsync(foundry);

        var (_, run, log) = await simulator.RunToEndAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct);

        run.Status.Should().Be(Norvia.Simulator.Domain.Runs.SimulationRunStatus.Completed);
        var declined = log.Where(line => line.Outcome == Norvia.Simulator.Domain.Log.LogOutcome.Declined).ToList();
        declined.Should().HaveCount(9);
        foreach (var line in declined)
        {
            var booking = (await fixture.ShipmentAsync(line.ShipmentNumber!, Ct))!["carrierBookings"]!.AsArray().Single()!;
            booking["reasonText"]!.GetValue<string>().Should().StartWith("Fake ", "the text is one the Foundry agent wrote");
        }
    }

    [Fact]
    public async Task ThePersonasAgent_HasNoTool_SoItsIdentityHasNothingToCallAndHoldsNoRole()
    {
        var foundry = new FakeFoundry();
        await using var simulator = await StartAsync(foundry);

        await simulator.Requests.PreviewAsync(Runs.Carried(orders: 9, Runs.NextSeed(), Declines), Ct);

        // The agent's definition declares no tool, so the deploy script publishes it without a server, and no role is assigned for it.
        var root = RepositoryRoot();
        var definition = await File.ReadAllTextAsync(Path.Combine(root, "agents", "simulator-personas.agent.yaml"), Ct);
        definition.Split('\n').Should().NotContain(line => line.StartsWith("tools:", StringComparison.Ordinal), "the agent has no tool");
        var estate = await File.ReadAllTextAsync(Path.Combine(root, "infra", "estate", "foundry.bicep"), Ct);
        estate.Should().NotContain("simulator-personas", "no Azure role or Entra grant is made for the agent");
        // What the Simulator sends offers the model no tool either.
        foundry.Requests.Should().NotBeEmpty();
        foreach (var request in foundry.Requests)
        {
            JsonNode.Parse(request.Body)!.AsObject().Should().NotContainKey("tools").And.NotContainKey("tool_choice");
        }
    }

    private Task<SimulatorHost> StartAsync(FakeFoundry foundry, params (string Name, string Value)[] settings)
    {
        var all = new Dictionary<string, string>
        {
            ["Agents:Foundry:ProjectEndpoint"] = Project,
            ["Agents:ManagedIdentityClientId"] = Guid.NewGuid().ToString(),
        };
        foreach (var (name, value) in settings)
        {
            all[name] = value;
        }

        return fixture.StartSimulatorWithAgentsAsync(
            services =>
            {
                services.AddSingleton(foundry.Credential);
                // The typed client of the Foundry client is named after its service type.
                services.AddHttpClient(nameof(IAgentClient)).ConfigurePrimaryHttpMessageHandler(() => foundry);
            },
            all,
            Ct,
            SeedEdits.OnlyShippers(PricedShipper));
    }

    private static string RepositoryRoot()
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

    private sealed record Recorded(string Uri, string? Authorization, string Body);

    /// <summary>A Foundry project's Responses endpoint, and the token it takes: it records what it is asked and answers with texts for the profile and the category.</summary>
    private sealed class FakeFoundry : HttpMessageHandler
    {
        private readonly object _lock = new();
        private readonly List<Recorded> _requests = [];
        private readonly List<string> _scopes = [];
        private int _answers;

        public IReadOnlyList<Recorded> Requests
        {
            get
            {
                lock (_lock)
                {
                    return [.. _requests];
                }
            }
        }

        public IReadOnlyList<string> Scopes
        {
            get
            {
                lock (_lock)
                {
                    return [.. _scopes];
                }
            }
        }

        public bool Hang { get; init; }

        public HttpStatusCode? Failure { get; init; }

        /// <summary>The text the agent answers with, from the question; by default a JSON list of texts.</summary>
        public Func<string, string>? Text { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            int answer;
            lock (_lock)
            {
                _requests.Add(new Recorded(request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), body));
                answer = ++_answers;
            }

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Failure is { } failure)
            {
                return new HttpResponseMessage(failure);
            }

            var question = JsonNode.Parse(body)!["input"]![0]!["content"]!.GetValue<string>();
            var text = Text?.Invoke(question) ?? Texts(question, answer);
            var response = new JsonObject
            {
                ["id"] = $"resp_{answer}",
                ["status"] = "completed",
                ["output"] = new JsonArray(new JsonObject
                {
                    ["type"] = "message",
                    ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }),
                }),
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json")) };
        }

        private static string Texts(string question, int answer)
        {
            var count = int.Parse(CountLine().Match(question).Groups["count"].Value, CultureInfo.InvariantCulture);
            var profile = ProfileLine().Match(question).Groups["profile"].Value.Trim();
            var category = CategoryLine().Match(question).Groups["category"].Value.Trim();
            var texts = new JsonArray([.. Enumerable.Range(1, count).Select(index => (JsonNode)$"Fake {profile} {category} text {index} of answer {answer}")]);
            return new JsonObject { ["texts"] = texts }.ToJsonString();
        }

        public TokenCredential Credential => new FakeCredential(this);

        private void RecordScopes(TokenRequestContext context)
        {
            lock (_lock)
            {
                _scopes.AddRange(context.Scopes);
            }
        }

        private sealed class FakeCredential(FakeFoundry owner) : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token(requestContext);

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token(requestContext));

            private AccessToken Token(TokenRequestContext context)
            {
                owner.RecordScopes(context);
                return new AccessToken("foundry-token", DateTimeOffset.MaxValue);
            }
        }
    }
}
