using System.Text.Json.Nodes;

namespace Norvia.Integration.LocalConsigneeNotificationWorker.UnitTests;

public sealed class SmtpConsigneeMailboxTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "norvia-consignee-mail-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SmtpConsigneeMailbox Mailbox(Action<MailOptions>? change = null)
    {
        var options = new ConsigneeNotificationWorkerOptions { Mail = new() { PickupDirectory = _directory } };
        change?.Invoke(options.Mail);
        return new SmtpConsigneeMailbox(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static JsonObject Email() => new() { ["to"] = "inkoop@dezwaan.example", ["subject"] = "Your goods have been collected", ["plainText"] = "See https://portal.norvia.example/track/7QK2M9XH4D8VWRBT" };

    [Fact]
    public async Task SendAsync_WithNoMailServer_WritesAnEmlFileToTheFolder()
    {
        var answer = await Mailbox().SendAsync(Email(), TestContext.Current.CancellationToken);

        answer.Should().Be(new MailAnswer(true, false));
        var file = Directory.GetFiles(_directory, "*.eml").Should().ContainSingle().Subject;
        var text = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        text.Should().Contain("To: inkoop@dezwaan.example").And.Contain("From: tracking@norvia.example").And.Contain("Subject: Your goods have been collected").And.Contain("/track/7QK2M9XH4D8VWRBT");
    }

    [Fact]
    public async Task SendAsync_AMailServerThatIsNotThere_IsRetryableAndNeverSaysWhy()
    {
        // Port 1 on the loopback address refuses at once.
        var answer = await Mailbox(mail => { mail.Host = "127.0.0.1"; mail.Port = 1; }).SendAsync(Email(), TestContext.Current.CancellationToken);

        answer.Succeeded.Should().BeFalse();
        answer.IsRetryable.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_NoEmail_Throws()
    {
        var send = () => Mailbox().SendAsync(null!, TestContext.Current.CancellationToken);

        await send.Should().ThrowAsync<ArgumentNullException>();
    }
}
