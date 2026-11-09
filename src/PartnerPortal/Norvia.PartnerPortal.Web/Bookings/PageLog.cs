namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>The pages' logs. Here rather than in the pages: the logging source generator can't see Razor's generated code.</summary>
internal static partial class PageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the booking form for Persona {PersonaId} failed")]
    public static partial void LoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Asking for a Booking draft for Persona {PersonaId} failed")]
    public static partial void DraftFromTextFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending or recording a booking by Persona {PersonaId} failed")]
    public static partial void SubmitFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the Personas for the navbar failed")]
    public static partial void PersonasFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the booking requests of Persona {PersonaId} failed")]
    public static partial void BookingRequestsLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the bookings of Persona {PersonaId} failed")]
    public static partial void BookingsLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending or recording Persona {PersonaId}'s answer to {CarrierBookingNumber} failed")]
    public static partial void AnswerFailed(ILogger logger, Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the answer mode of Persona {PersonaId} failed")]
    public static partial void AnswerModeLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the answer mode of Persona {PersonaId} failed")]
    public static partial void AnswerModeSaveFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the tracking mode of Persona {PersonaId} failed")]
    public static partial void TrackingModeLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the tracking mode of Persona {PersonaId} failed")]
    public static partial void TrackingModeSaveFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the Shipments of Persona {PersonaId} failed")]
    public static partial void ShipmentsLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending or recording Persona {PersonaId}'s Tracking event for {CarrierBookingNumber} failed")]
    public static partial void TrackingFailed(ILogger logger, Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending Persona {PersonaId}'s Proof of delivery for {CarrierBookingNumber} failed")]
    public static partial void ProofOfDeliveryFailed(ILogger logger, Exception exception, string personaId, string carrierBookingNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the draft invoice of Persona {PersonaId} failed")]
    public static partial void DraftInvoiceLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending or keeping Persona {PersonaId}'s Carrier invoice {InvoiceNumber} failed")]
    public static partial void CarrierInvoiceFailed(ILogger logger, Exception exception, string personaId, string invoiceNumber);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the Carrier invoices of Persona {PersonaId} failed")]
    public static partial void CarrierInvoicesLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the invoices of Persona {PersonaId} failed")]
    public static partial void InvoicesLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the notifications of Persona {PersonaId} failed")]
    public static partial void NotificationsLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the tracking of Persona {PersonaId} failed")]
    public static partial void TrackingLoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the faults failed")]
    public static partial void FaultsLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the Drivers' sign-in details failed")]
    public static partial void DriverSignInsLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Switching the fault {Fault} failed")]
    public static partial void FaultSwitchFailed(ILogger logger, Exception exception, string fault);
}
