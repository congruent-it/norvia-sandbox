using Ardalis.Result;
using Ardalis.SharedKernel;
using Mediator;
using Norvia.PartnerPortal.Application.Bookings;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.CarrierBookings;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.CarrierShipments.SendProofOfDelivery;

public sealed class SendProofOfDeliveryHandler(
    IReadRepository<CarrierBookingRequest> repository,
    INorviaPartnerApi partnerApi,
    IPersonaDirectory personas,
    TimeProvider time)
    : ICommandHandler<SendProofOfDeliveryCommand, Result<SentProofOfDelivery>>
{
    /// <summary>What the TMS takes: 1 to 5 files of at most 10 MB each.</summary>
    public const int MaxFiles = 5;

    public const int MaxFileBytes = 10 * 1024 * 1024;

    public async ValueTask<Result<SentProofOfDelivery>> Handle(SendProofOfDeliveryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (personas.Find(new PersonaId(command.PersonaId)) is not { PartyType: PartyType.Carrier } carrier)
        {
            return Result.NotFound($"There is no Carrier Persona '{command.PersonaId}'.");
        }

        var request = await repository.FirstOrDefaultAsync(new CarrierBookingRequestToAnswerSpec(carrier.Id, command.CarrierBookingNumber), cancellationToken);
        if (request is not { Status: CarrierBookingRequestStatus.Confirmed, CarrierBookingReference: { } reference })
        {
            return Result.NotFound($"{carrier.DisplayName} has no Shipment for booking {command.CarrierBookingNumber}.");
        }

        if (Invalidity(command) is { } invalid)
        {
            return Result.Invalid(new ValidationError(invalid));
        }

        if (request.Details.Delivery is not { City: { Length: > 0 } place, Postcode: { Length: > 0 } postcode, Country: { Length: > 0 } country })
        {
            return Result.Invalid(new ValidationError("The Shipment's delivery address has no place, postcode and country to deliver at."));
        }

        var now = time.GetUtcNow();
        var proof = new CarrierProofOfDelivery(
            Guid.CreateVersion7(now).ToString("N"), reference, request.CarrierBookingNumber, command.ReceivedBy.Trim(), now,
            place, postcode, country, command.Condition, Remark(command),
            [.. command.Files.Select(file => new CarrierProofOfDeliveryFile(file.Name, file.ContentType, file.Content))]);
        var reply = await partnerApi.SubmitProofOfDeliveryAsync(carrier, request.ShipmentNumber, proof, cancellationToken);

        // A Proof of delivery never changes the Shipment, so the portal keeps nothing of it.
        return new SentProofOfDelivery(CarrierShipmentView.From(request), proof, reply);
    }

    // A remark belongs to reservations: a clean Proof of delivery carries none, whatever was typed before the condition changed.
    private static string? Remark(SendProofOfDeliveryCommand command) =>
        command.Condition == ProofOfDeliveryCondition.Reservations && !string.IsNullOrWhiteSpace(command.Remark) ? command.Remark.Trim() : null;

    private static string? Invalidity(SendProofOfDeliveryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ReceivedBy))
        {
            return "A Proof of delivery needs the name of the person who received the goods.";
        }

        if (command.Condition == ProofOfDeliveryCondition.Reservations && Remark(command) is null)
        {
            return "A Proof of delivery with reservations needs a remark.";
        }

        if (command.Files.Count is 0 or > MaxFiles)
        {
            return $"A Proof of delivery has 1 to {MaxFiles} files, not {command.Files.Count}.";
        }

        if (command.Files.FirstOrDefault(file => file.Content.Length == 0 || file.Content.Length > MaxFileBytes) is { } file)
        {
            return $"File {file.Name} is empty or over 10 MB.";
        }

        return null;
    }
}
