using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.CarrierBookings.Answer;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;

namespace Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;

public sealed partial class AnswerDueCarrierBookingsHandler(
    IRepository<CarrierBookingRequest> repository,
    IPersonaDirectory personas,
    IMediator mediator,
    TimeProvider time,
    ILogger<AnswerDueCarrierBookingsHandler> logger)
    : ICommandHandler<AnswerDueCarrierBookingsCommand, AutoAnswerOutcome>
{
    /// <summary>How long an answer that Norvia didn't take waits before it is sent again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    public async ValueTask<AutoAnswerOutcome> Handle(AnswerDueCarrierBookingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = time.GetUtcNow();
        foreach (var due in await repository.ListAsync(new CarrierBookingRequestsDueForAutoAnswerSpec(now), cancellationToken))
        {
            await AnswerAsync(due, now, cancellationToken);
        }

        var next = await repository.FirstOrDefaultAsync(new NextAutoAnswerSpec(), cancellationToken);
        return new AutoAnswerOutcome(next?.AutoAnswerDueAt);
    }

    private async Task AnswerAsync(CarrierBookingRequest due, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var carrier = personas.Find(due.PersonaId);
        if (carrier?.Carrier is null || due.AutoAnswerDue(carrier.Carrier.BookingReferencePrefix, now) is not { } answer)
        {
            return;
        }

        var answered = await mediator.Send(new AnswerCarrierBookingCommand(carrier.Id, due.CarrierBookingNumber, answer), cancellationToken);
        if (answered.IsSuccess && !answered.Value.Request.IsOpen)
        {
            return;
        }

        // Norvia didn't take it, or the portal couldn't keep Norvia's reply: try again shortly, rather than every time we look.
        LogDeferred(carrier.Id.Value, due.CarrierBookingNumber, answered.IsSuccess ? answered.Value.Reply?.Result : null);
        var stillOpen = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(carrier.Id, due.CarrierBookingNumber), cancellationToken);
        if (stillOpen is { IsOpen: true })
        {
            stillOpen.DeferAutoAnswer(now + RetryAfter);
            await repository.UpdateAsync(stillOpen, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Persona {PersonaId}'s automatic answer to {CarrierBookingNumber} wasn't taken (Norvia replied {Result}); it will be sent again")]
    private partial void LogDeferred(string personaId, string carrierBookingNumber, CarrierAnswerResult? result);
}
