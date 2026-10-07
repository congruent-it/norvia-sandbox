using Norvia.Tms.Application.Exceptions.ListOpen;
using Norvia.Tms.Application.Shipments;
using Norvia.Tms.Domain.Exceptions;
using Norvia.Tms.Web.UnitTests.TransportOrders;

namespace Norvia.Tms.Web.UnitTests.Exceptions;

internal static class OpenExceptionSamples
{
    // A failed delivery of a Booked Shipment of Kessler Spedition, opened at 11:45 on 24 September 2026 in Rotterdam (UTC+2).
    public static OpenExceptionSummary DeliveryFailed(string number = "EX-0000005", string shipmentNumber = "SH-0000012") => new(
        number,
        shipmentNumber,
        Samples.BrouwerDranken,
        new CarriedByView(AssignedTo.Carrier, Samples.Kessler),
        ExceptionType.DeliveryFailed,
        new DateTimeOffset(2026, 9, 24, 9, 45, 0, TimeSpan.Zero),
        CanCancelCarrierBooking: true,
        CanAgreeDeliveryWindow: false,
        NumberOfCauses: 1,
        AfterDelivered: false,
        CauseCleared: false,
        Notes: [],
        LastShipperNotificationStatus: null);
}
