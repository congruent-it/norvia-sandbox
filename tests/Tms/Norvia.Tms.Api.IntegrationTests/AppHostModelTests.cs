using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;

namespace Norvia.Tms.Api.IntegrationTests;

/// <summary>Checks the AppHost's resource model without starting anything.</summary>
public sealed class AppHostModelTests
{
    [Fact]
    public async Task EveryTopic_HasASubscription_SoTheServiceBusEmulatorStarts()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Norvia_AppHost>(TestContext.Current.CancellationToken);

        var subscribedTopics = builder.Resources.OfType<AzureServiceBusSubscriptionResource>().Select(s => s.Parent).ToHashSet();

        builder.Resources.OfType<AzureServiceBusTopicResource>().Should().OnlyContain(topic => subscribedTopics.Contains(topic));
    }
}
