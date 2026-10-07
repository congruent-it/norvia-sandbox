using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierBookings;

/// <summary>What the Carrier is told after answering a request, in plain words: whether Norvia recorded it, and if not, why.</summary>
/// <param name="Text">The notice.</param>
/// <param name="Intent">How it looks: success, a warning for what the Carrier may try again, an error for what it can't.</param>
/// <param name="MessageId">The ID Norvia gave the answer, for asking Norvia about it; null when Norvia never replied.</param>
/// <param name="AnswerAgain">Whether the form stays open: the answer reached nobody, so the Carrier can send it again.</param>
public sealed record AnswerNotice(string Text, MessageIntent Intent, string? MessageId, bool AnswerAgain)
{
    public static AnswerNotice Of(string carrierBookingNumber, CarrierAnswer answer, AnsweredCarrierBooking answered)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(answered);

        if (answered.Reply is not { } reply)
        {
            return new($"{carrierBookingNumber} is no longer open, so nothing was sent to Norvia.", MessageIntent.Info, MessageId: null, AnswerAgain: false);
        }

        var what = answer is CarrierAnswer.Confirmation ? "confirmation" : "rejection";
        return reply.Result switch
        {
            CarrierAnswerResult.Recorded when !answered.Recorded =>
                new($"Norvia recorded your {what} of {carrierBookingNumber}, but the portal couldn't keep it, so the inbox still shows the request as open.", MessageIntent.Warning, reply.MessageId, AnswerAgain: false),
            CarrierAnswerResult.Recorded when reply.Repeated =>
                new($"Norvia already had your {what} of {carrierBookingNumber}. Nothing was recorded again.", MessageIntent.Success, reply.MessageId, AnswerAgain: false),
            CarrierAnswerResult.Recorded =>
                new($"Norvia recorded your {what} of {carrierBookingNumber}.", MessageIntent.Success, reply.MessageId, AnswerAgain: false),
            CarrierAnswerResult.BookingNotOpen =>
                new($"Norvia no longer has {carrierBookingNumber} open: it was withdrawn while you were answering, or has been closed. Nothing was recorded, and the inbox shows the request as withdrawn by Norvia.", MessageIntent.Warning, reply.MessageId, AnswerAgain: false),
            CarrierAnswerResult.ReferenceInUse =>
                new($"Norvia did not record your {what} of {carrierBookingNumber}: you already hold that reference on another confirmed booking. Confirm again with a reference of its own.", MessageIntent.Warning, reply.MessageId, AnswerAgain: true),
            CarrierAnswerResult.NotReceived =>
                new($"Norvia did not receive your {what} of {carrierBookingNumber}, so nothing was recorded. Send it again in a moment.", MessageIntent.Warning, reply.MessageId, AnswerAgain: true),
            _ => new(reply.Text ?? "Norvia refused the answer.", MessageIntent.Error, reply.MessageId, AnswerAgain: false),
        };
    }
}
