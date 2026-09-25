using System.Globalization;
using Ardalis.Result;
using Mediator;
using Norvia.PartnerPortal.Application.Personas;
using Norvia.PartnerPortal.Domain.Personas;

namespace Norvia.PartnerPortal.Application.Bookings.Draft;

public sealed class DraftBookingHandler(IPersonaDirectory personas, TimeProvider time) : IQueryHandler<DraftBookingQuery, Result<BookingRequest>>
{
    public ValueTask<Result<BookingRequest>> Handle(DraftBookingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (personas.Find(new PersonaId(query.PersonaId)) is not { } persona)
        {
            return ValueTask.FromResult<Result<BookingRequest>>(Result.NotFound($"There is no Persona '{query.PersonaId}'."));
        }

        var now = time.GetUtcNow();
        // Tomorrow, so the collection window is never in the past whatever the time of day in Europe.
        var tomorrow = now.UtcDateTime.Date.AddDays(1);
        // The party's own premises, where goods are collected, and one of its Consignees to deliver to.
        var own = persona.SavedAddresses.FirstOrDefault(address => address.Consignee is null);
        var consignee = persona.SavedAddresses.FirstOrDefault(address => address.Consignee is not null);

        return ValueTask.FromResult<Result<BookingRequest>>(new BookingRequest
        {
            // A new reference each time: sending one again is a repeat of the booking that holds it.
            ShipperReference = "BK-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            CollectionAddress = AddressOf(own),
            CollectionWindow = new LocalWindow(tomorrow.AddHours(8), tomorrow.AddHours(12)),
            Consignee = consignee?.Consignee is { } receiver ? new BookingConsignee(receiver.Name, receiver.ContactName, receiver.Phone) : new BookingConsignee(),
            DeliveryAddress = AddressOf(consignee),
            GoodsLines = [new BookingGoodsLine(PackageType.Pallet, 2, 850m, new PackageDimensions(120, 80, 150))],
        });
    }

    private static BookingAddress AddressOf(SavedAddress? address) =>
        address is null ? new BookingAddress() : new BookingAddress(address.Street, address.Postcode, address.City, address.Country);
}
