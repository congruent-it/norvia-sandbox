using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.CarrierShipments.Send;
using Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierShipments.AutoProgress;

public sealed partial class SendDueTrackingHandler(
    IRepository<CarrierBookingRequest> repository,
    IMediator mediator,
    ISampleCmr sampleCmr,
    TimeProvider time,
    ILogger<SendDueTrackingHandler> logger)
    : ICommandHandler<SendDueTrackingCommand, AutoProgressOutcome>
{
    /// <summary>How long an event that Norvia didn't take waits before it is sent again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    /// <summary>Who an automatic Proof of delivery says received the goods.</summary>
    public const string ReceivedBy = "Receiving dock";

    public async ValueTask<AutoProgressOutcome> Handle(SendDueTrackingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = time.GetUtcNow();
        foreach (var due in await repository.ListAsync(new CarrierBookingRequestsDueForAutoProgressSpec(now), cancellationToken))
        {
            await SendAsync(due, now, cancellationToken);
        }

        var next = await repository.FirstOrDefaultAsync(new NextAutoProgressSpec(), cancellationToken);
        return new AutoProgressOutcome(next?.AutoProgressDueAt);
    }

    private async Task SendAsync(CarrierBookingRequest due, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (due.AutoProofOfDeliveryDue(now))
        {
            await SendProofOfDeliveryAsync(due, now, cancellationToken);
            return;
        }

        if (due.AutoProgressDue(now) is not { } type)
        {
            return;
        }

        var sent = await mediator.Send(new SendTrackingCommand(due.PersonaId.Value, due.CarrierBookingNumber, type), cancellationToken);
        if (sent.IsSuccess && sent.Value.Reply.IsAccepted && sent.Value.Recorded)
        {
            return;
        }

        // Norvia didn't take it, or the portal couldn't keep it: try again shortly, rather than every time we look.
        LogDeferred(due.PersonaId.Value, due.CarrierBookingNumber, type, sent.IsSuccess ? sent.Value.Reply.Result : null);
        await DeferAsync(due, now, cancellationToken);
    }

    // A clean Proof of delivery with the sample CMR, through the same send use case as the form.
    private async Task SendProofOfDeliveryAsync(CarrierBookingRequest due, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sent = await mediator.Send(
            new SendProofOfDeliveryCommand(due.PersonaId.Value, due.CarrierBookingNumber, ReceivedBy, ProofOfDeliveryCondition.Clean, null, [sampleCmr.Read()]),
            cancellationToken);
        if (sent.IsSuccess && sent.Value.Reply.IsAccepted)
        {
            var delivered = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(due.PersonaId, due.CarrierBookingNumber), cancellationToken);
            if (delivered is not null)
            {
                delivered.ReportAutoProofOfDelivery();
                await repository.UpdateAsync(delivered, cancellationToken);
            }

            return;
        }

        LogProofOfDeliveryDeferred(due.PersonaId.Value, due.CarrierBookingNumber, sent.IsSuccess ? sent.Value.Reply.Result : null);
        await DeferAsync(due, now, cancellationToken);
    }

    private async Task DeferAsync(CarrierBookingRequest due, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stillDue = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(due.PersonaId, due.CarrierBookingNumber), cancellationToken);
        if (stillDue is { AutoProgressDueAt: not null })
        {
            stillDue.DeferAutoProgress(now + RetryAfter);
            await repository.UpdateAsync(stillDue, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persona {PersonaId}'s automatic Proof of delivery for {CarrierBookingNumber} wasn't taken (Norvia replied {Result}); it will be sent again")]
    private partial void LogProofOfDeliveryDeferred(string personaId, string carrierBookingNumber, TrackingResult? result);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persona {PersonaId}'s automatic {Type} event for {CarrierBookingNumber} wasn't taken (Norvia replied {Result}); it will be sent again")]
    private partial void LogDeferred(string personaId, string carrierBookingNumber, TrackingEventType type, TrackingResult? result);
}
