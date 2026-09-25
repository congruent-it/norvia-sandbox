namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>The pages' logs. Here rather than in the pages: the logging source generator can't see Razor's generated code.</summary>
internal static partial class PageLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the booking form for Persona {PersonaId} failed")]
    public static partial void LoadFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending or recording a booking by Persona {PersonaId} failed")]
    public static partial void SubmitFailed(ILogger logger, Exception exception, string personaId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reading the Personas for the navbar failed")]
    public static partial void PersonasFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the bookings of Persona {PersonaId} failed")]
    public static partial void BookingsLoadFailed(ILogger logger, Exception exception, string personaId);
}
