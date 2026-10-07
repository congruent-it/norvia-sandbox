using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Bookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.Submit;

public sealed partial class SubmitBookingHandler(
    IRepository<Booking> repository,
    INorviaPartnerApi partnerApi,
    IPersonaDirectory personas,
    TimeProvider time,
    ILogger<SubmitBookingHandler> logger)
    : ICommandHandler<SubmitBookingCommand, Result<BookingView>>
{
    public async ValueTask<Result<BookingView>> Handle(SubmitBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(command.PersonaId) is not { } persona)
        {
            return Result.NotFound($"There is no Persona '{command.PersonaId}'.");
        }

        var sentAt = time.GetUtcNow();
        var answer = await partnerApi.SubmitAsync(persona, command.Booking, cancellationToken);
        var booking = Booking.Record(persona.Id, command.Booking.ShipperReference, sentAt, answer);
        try
        {
            await repository.AddAsync(booking, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Norvia has answered, and its answer stands: losing the portal's own record of it mustn't hide it.
            using (logger.BeginScope(TrackingKeysOf(persona, answer)))
            {
                LogNotRecorded(exception, persona.Id.Value, answer.Result);
            }

            return BookingView.From(booking) with { Recorded = false };
        }

        return BookingView.From(booking);
    }

    /// <summary>The tracking contract's keys the portal knows (coding guidelines §7), so the error joins Norvia's Message trail.</summary>
    private static Dictionary<string, object?> TrackingKeysOf(Persona persona, NorviaAnswer answer)
    {
        var keys = new Dictionary<string, object?> { ["flow"] = "transport-order-intake", ["partyId"] = persona.PartyId };
        if (answer.MessageId is { } correlationId)
        {
            keys["correlationId"] = correlationId;
        }

        return keys;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Norvia answered a booking by Persona {PersonaId} with {Result}, but the portal couldn't record it")]
    private partial void LogNotRecorded(Exception exception, string personaId, BookingResult result);
}
