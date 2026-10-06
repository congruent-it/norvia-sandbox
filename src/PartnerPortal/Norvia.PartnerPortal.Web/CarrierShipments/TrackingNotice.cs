using Microsoft.FluentUI.AspNetCore.Components;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Web.CarrierShipments;

/// <summary>What the Carrier is told after sending a Tracking event or a Proof of delivery: that Norvia took it in, and no more, or why not.</summary>
/// <param name="Text">The notice.</param>
/// <param name="Intent">How it looks.</param>
/// <param name="MessageId">The ID Norvia gave the message, for asking Norvia about it; null when Norvia never replied.</param>
/// <param name="Subject">What was sent.</param>
public sealed record TrackingNotice(string Text, MessageIntent Intent, string? MessageId, string Subject = "Tracking event")
{
    public static TrackingNotice Of(SentTrackingEvent sent)
    {
        ArgumentNullException.ThrowIfNull(sent);

        var what = $"{TrackingEventText.Button(sent.Event.Type)} for {sent.Event.CarrierBookingNumber}";
        var reply = sent.Reply;
        return reply.Result switch
        {
            TrackingResult.Accepted when !sent.Recorded =>
                new($"Norvia accepted {what}, but the portal couldn't keep it, so the Shipment still shows as it was.", MessageIntent.Warning, reply.MessageId),
            TrackingResult.Accepted =>
                new($"Norvia accepted {what}. It tells you no more: the TMS records it afterwards.", MessageIntent.Success, reply.MessageId),
            TrackingResult.NotReceived =>
                new($"Norvia did not receive {what}. Send it again in a moment.", MessageIntent.Warning, reply.MessageId),
            _ => new(reply.Text ?? "Norvia refused the Tracking event.", MessageIntent.Error, reply.MessageId),
        };
    }

    public static TrackingNotice Of(SentProofOfDelivery sent)
    {
        ArgumentNullException.ThrowIfNull(sent);

        const string subject = "Proof of delivery";
        var what = $"the Proof of delivery for {sent.ProofOfDelivery.CarrierBookingNumber}";
        var reply = sent.Reply;
        return reply.Result switch
        {
            TrackingResult.Accepted =>
                new($"Norvia accepted {what}. It tells you no more: the TMS records it afterwards.", MessageIntent.Success, reply.MessageId, subject),
            TrackingResult.NotReceived =>
                new($"Norvia did not receive {what}. Send it again in a moment.", MessageIntent.Warning, reply.MessageId, subject),
            _ => new(reply.Text ?? "Norvia refused the Proof of delivery.", MessageIntent.Error, reply.MessageId, subject),
        };
    }
}
