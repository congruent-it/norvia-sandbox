using System.Net.Mail;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Norvia.Integration.LocalConsigneeNotificationWorker;

/// <summary>Sends the Consignee's email, standing in for the Azure Communication Services email resource the workflow posts it to.</summary>
internal interface IConsigneeMailbox
{
    /// <param name="email">The email as the map makes it: <c>to</c>, <c>subject</c> and <c>plainText</c>.</param>
    Task<MailAnswer> SendAsync(JsonObject email, CancellationToken cancellationToken);
}

/// <param name="Succeeded">Whether the mail server took the email.</param>
/// <param name="IsRetryable">Whether sending again may work: the server is busy or unavailable. Otherwise it refused the email.</param>
internal sealed record MailAnswer(bool Succeeded, bool IsRetryable);

/// <summary>
/// Sends over SMTP to the mail catcher the AppHost runs, or writes an .eml file to a folder when no SMTP server is set. Its errors are
/// reported as retryable or refused and nothing more: a mail server's message can hold the address.
/// </summary>
internal sealed class SmtpConsigneeMailbox(IOptions<ConsigneeNotificationWorkerOptions> options) : IConsigneeMailbox
{
    public async Task<MailAnswer> SendAsync(JsonObject email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var mail = options.Value.Mail;
        try
        {
            using var message = new MailMessage(mail.SenderAddress, email["to"]!.GetValue<string>(), email["subject"]!.GetValue<string>(), email["plainText"]!.GetValue<string>());
            using var client = Client(mail);
            await client.SendMailAsync(message, cancellationToken);
            return new(true, false);
        }
        catch (SmtpException exception)
        {
            return new(false, exception.StatusCode is SmtpStatusCode.GeneralFailure or SmtpStatusCode.ServiceNotAvailable or SmtpStatusCode.MailboxBusy or SmtpStatusCode.InsufficientStorage or SmtpStatusCode.LocalErrorInProcessing);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            return new(false, true);
        }
    }

    private static SmtpClient Client(MailOptions mail)
    {
        if (!string.IsNullOrWhiteSpace(mail.Host))
        {
            return new SmtpClient(mail.Host, mail.Port);
        }

        var directory = string.IsNullOrWhiteSpace(mail.PickupDirectory) ? Path.Combine(Path.GetTempPath(), "norvia-consignee-mail") : mail.PickupDirectory;
        Directory.CreateDirectory(directory);
        return new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = directory };
    }
}
