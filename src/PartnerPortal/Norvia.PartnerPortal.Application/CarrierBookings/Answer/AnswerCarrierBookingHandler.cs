using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierBookings.Answer;

public sealed partial class AnswerCarrierBookingHandler(
    IRepository<CarrierBookingRequest> repository,
    INorviaPartnerApi partnerApi,
    IPersonaDirectory personas,
    TimeProvider time,
    ILogger<AnswerCarrierBookingHandler> logger)
    : ICommandHandler<AnswerCarrierBookingCommand, Result<AnsweredCarrierBooking>>
{
    public async ValueTask<Result<AnsweredCarrierBooking>> Handle(AnswerCarrierBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(new PersonaId(command.PersonaId)) is not { PartyType: PartyType.Carrier } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{command.PersonaId}'.");
        }

        var request = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(carrier.Id, command.CarrierBookingNumber), cancellationToken);
        if (request is null)
        {
            return Result.NotFound($"{carrier.DisplayName} has no booking request {command.CarrierBookingNumber}.");
        }

        // Answered or withdrawn in the meantime, for example by a second click: nothing more to send.
        if (!request.IsOpen)
        {
            return new AnsweredCarrierBooking(CarrierBookingRequestView.From(request), Reply: null);
        }

        var reply = await partnerApi.AnswerAsync(carrier, command.CarrierBookingNumber, request.ShipmentNumber, command.Answer, cancellationToken);
        var changed = reply.Result switch
        {
            CarrierAnswerResult.Recorded => request.Answer(command.Answer, reply.MessageId, time.GetUtcNow()),
            CarrierAnswerResult.BookingNotOpen => request.WithdrawnByNorvia(reply.MessageId, time.GetUtcNow()),
            _ => false,
        };
        if (!changed)
        {
            return new AnsweredCarrierBooking(CarrierBookingRequestView.From(request), reply);
        }

        try
        {
            await repository.UpdateAsync(request, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Norvia has replied, and its reply stands: losing the portal's own record of it mustn't hide it.
            using (logger.BeginScope(CarrierAnswerTracking.KeysOf(carrier, request.CarrierBookingNumber, request.ShipmentNumber, reply.MessageId)))
            {
                LogNotRecorded(exception, carrier.Id.Value, request.CarrierBookingNumber, reply.Result);
            }

            return new AnsweredCarrierBooking(CarrierBookingRequestView.From(request), reply, Recorded: false);
        }

        return new AnsweredCarrierBooking(CarrierBookingRequestView.From(request), reply);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Norvia replied {Result} to Persona {PersonaId}'s answer to {CarrierBookingNumber}, but the portal couldn't record it")]
    private partial void LogNotRecorded(Exception exception, string personaId, string carrierBookingNumber, CarrierAnswerResult result);
}
