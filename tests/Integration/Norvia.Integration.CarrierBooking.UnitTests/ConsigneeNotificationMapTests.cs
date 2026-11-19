using System.Text.Json.Nodes;

namespace Norvia.Integration.CarrierBooking.UnitTests;

public class ConsigneeNotificationMapTests
{
    private const string BaseUrl = "https://portal.norvia.example";
    private const string Content = """{"transportOrderNumber":"TO-0000007","email":"inkoop@dezwaan.example","trackingCode":"7QK2M9XH4D8VWRBT"}""";

    private static string Requested(string sample = "collected", Action<JsonObject>? change = null)
    {
        var requested = JsonNode.Parse(Contracts.Read($"CanonicalModel/samples/consignee-notification-requested/{sample}.json"))!.AsObject();
        change?.Invoke(requested);
        return requested.ToJsonString();
    }

    [Theory]
    [InlineData("collected", "Collected")]
    [InlineData("delivery-window-agreed", "DeliveryWindowAgreed")]
    public void ToNotification_TheSamples_GiveTheTransportOrderAndTheReason(string sample, string reason)
    {
        var notification = ConsigneeNotificationMap.ToNotification(Requested(sample));

        notification!["transportOrderNumber"]!.GetValue<string>().Should().Be("TO-0000007");
        notification["reason"]!.GetValue<string>().Should().Be(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void ToNotification_TextThatIsNoEvent_GivesNothing(string text) =>
        ConsigneeNotificationMap.ToNotification(text).Should().BeNull();

    [Theory]
    [InlineData("transportOrderNumber")]
    [InlineData("reason")]
    public void ToNotification_AnEventMissingWhatItNeeds_GivesNothing(string field) =>
        ConsigneeNotificationMap.ToNotification(Requested(change: r => r.Remove(field))).Should().BeNull();

    [Fact]
    public void ToNotification_AnUnknownReason_GivesNothing() =>
        ConsigneeNotificationMap.ToNotification(Requested(change: r => r["reason"] = "Delivered")).Should().BeNull();

    [Fact]
    public void ToEmail_Collected_SendsTheLinkToTheConsignee()
    {
        var email = ConsigneeNotificationMap.ToEmail(Requested(), Content, BaseUrl);

        email!["to"]!.GetValue<string>().Should().Be("inkoop@dezwaan.example");
        email["subject"]!.GetValue<string>().Should().Be("Your goods have been collected: track them here");
        email["plainText"]!.GetValue<string>().Should().Contain("https://portal.norvia.example/track/7QK2M9XH4D8VWRBT").And.Contain("collected");
    }

    [Fact]
    public void ToEmail_DeliveryWindowAgreed_SaysSoAndSendsTheSameLink()
    {
        var email = ConsigneeNotificationMap.ToEmail(Requested("delivery-window-agreed"), Content, BaseUrl);

        email!["subject"]!.GetValue<string>().Should().Be("A delivery window has been agreed for your goods");
        email["plainText"]!.GetValue<string>().Should().Contain("/track/7QK2M9XH4D8VWRBT").And.Contain("delivery window");
    }

    [Fact]
    public void ToEmail_SendsTheLinkAndNothingElse_SoALostEmailLeaksNoMoreThanTheLink()
    {
        var text = ConsigneeNotificationMap.ToEmail(Requested(), Content, BaseUrl)!["plainText"]!.GetValue<string>();

        text.Should().NotContain("TO-0000007").And.NotContain("SH-0000012").And.NotContain("dezwaan").And.NotContain("2026");
    }

    [Theory]
    [InlineData("https://portal.norvia.example/")]
    [InlineData("https://portal.norvia.example")]
    public void ToEmail_ABaseUrlWithOrWithoutATrailingSlash_GivesOneSlashBeforeTrack(string baseUrl) =>
        ConsigneeNotificationMap.ToEmail(Requested(), Content, baseUrl)!["plainText"]!.GetValue<string>()
            .Should().Contain("https://portal.norvia.example/track/7QK2M9XH4D8VWRBT").And.NotContain("//track");

    [Theory]
    [InlineData("""{"transportOrderNumber":"TO-0000007","email":"","trackingCode":"7QK2M9XH4D8VWRBT"}""")]
    [InlineData("""{"transportOrderNumber":"TO-0000007","email":"inkoop@dezwaan.example","trackingCode":""}""")]
    [InlineData("""{"transportOrderNumber":"TO-0000007","trackingCode":"7QK2M9XH4D8VWRBT"}""")]
    [InlineData("""{"transportOrderNumber":"TO-0000007","email":"two@@x.example","trackingCode":"7QK2M9XH4D8VWRBT"}""")]
    [InlineData("""{"transportOrderNumber":"TO-0000007","email":"inkoop@dezwaan.example","trackingCode":"../../admin"}""")]
    [InlineData("not json")]
    public void ToEmail_ContentThatCantBeUsed_GivesNothing(string content) =>
        ConsigneeNotificationMap.ToEmail(Requested(), content, BaseUrl).Should().BeNull();

    [Fact]
    public void ToEmail_ContentOfAnotherTransportOrder_GivesNothing() =>
        ConsigneeNotificationMap.ToEmail(Requested(change: r => r["transportOrderNumber"] = "TO-0000099"), Content, BaseUrl).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    public void ToEmail_NoUsableBaseUrl_GivesNothing(string baseUrl) =>
        ConsigneeNotificationMap.ToEmail(Requested(), Content, baseUrl).Should().BeNull();

    [Fact]
    public void ToEmail_AnUnreadableEvent_GivesNothing() =>
        ConsigneeNotificationMap.ToEmail("not json", Content, BaseUrl).Should().BeNull();
}
