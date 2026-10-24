using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.PartnerPortal.Application;
using Norvia.PartnerPortal.Application.Faults;
using Norvia.PartnerPortal.Application.ShipperNotifications.Receive;
using Norvia.PartnerPortal.Domain.ShipperNotifications;

namespace Norvia.PartnerPortal.Web.ShipperNotifications;

/// <summary>
/// A Shipper Persona's notification endpoint: where Norvia's P7 Shipper notification Logic App sends a notification. It
/// only acknowledges: it answers a success once the notification is in the Shipper's inbox, and the same for one the inbox
/// has already, by its notification ID. The Shipper can't reply.
/// </summary>
public static partial class ShipperNotificationEndpoint
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string ApiKeyHeader = EndpointCredential.ApiKeyHeader;
    private const string Flow = "shipper-notification";

    public const string Route = "/shipper/v1/{shipperId}/notifications";

    /// <summary>Where a Shipper Persona's notifications are sent.</summary>
    public static string PathOf(string shipperId) => Route.Replace("{shipperId}", shipperId, StringComparison.Ordinal);

    public static IEndpointRouteBuilder MapShipperNotificationEndpoint(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous like the rest of the portal; what it asks of the caller is the Shipper's credential, checked below.
        endpoints.MapPost(Route, ReceiveAsync).AllowAnonymous();
        return endpoints;
    }

    public static async Task<Results<Ok<ReceiveShipperNotificationResult>, ProblemHttpResult, UnauthorizedHttpResult>> ReceiveAsync(
        string shipperId,
        [FromHeader(Name = ApiKeyHeader)] string? apiKey,
        [FromHeader(Name = CorrelationIdHeader)] string? correlationId,
        ShipperNotificationBody body,
        IConflictRetrier retrier,
        IOptions<ShipperEndpointOptions> options,
        IPartnerPortalFaults faults,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(retrier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(faults);
        ArgumentNullException.ThrowIfNull(loggers);

        var logger = loggers.CreateLogger(typeof(ShipperNotificationEndpoint));
        using var trail = logger.BeginScope(EndpointTracking.Keys(Flow, shipperId, correlationId, ("transportOrderNumber", body.TransportOrderNumber)));

        // The endpoint is down: it answers nothing but 503, and keeps nothing, so the Logic App reports the notification Not delivered.
        if (faults.ShipperEndpointDown)
        {
            using var down = logger.BeginScope(EndpointTracking.Down("ShipperEndpointUnavailable", PartnerPortalFaultNames.ShipperEndpointDown));
            LogDown(logger, shipperId);
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The Shipper's notification endpoint is unavailable.");
        }

        // Who the caller is comes first, so an unknown Shipper and a wrong credential look the same from outside.
        if (!EndpointCredential.IsOf(shipperId, apiKey, options.Value.ApiKeys))
        {
            using var refused = logger.BeginScope(EndpointTracking.Outcome("Refused", "CredentialInvalid"));
            LogRefused(logger, shipperId);
            return TypedResults.Unauthorized();
        }

        if (Problems(correlationId, body) is { Count: > 0 } problems)
        {
            using var invalid = logger.BeginScope(EndpointTracking.Outcome("Failed", "ShipperNotificationInvalid"));
            LogInvalid(logger, shipperId);
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "The notification is invalid.", detail: string.Join(' ', problems));
        }

        _ = ShipperExceptionCodes.TryParse(body.ExceptionCode, out var code);
        var command = new ReceiveShipperNotificationCommand(
            shipperId, Guid.Parse(body.NotificationId!), correlationId!,
            new ShipperNotificationContent(body.BookingRef!, body.TransportOrderNumber!, code, body.Message!, TimingOf(body), body.Contact!));
        // Two copies of one notification can arrive at once; the one that loses the race is told it is a repeat.
        var result = await retrier.RunAsync(mediator => mediator.Send(command, cancellationToken).AsTask());
        if (!result.IsSuccess)
        {
            using var unknown = logger.BeginScope(EndpointTracking.Outcome("Failed", "ShipperUnknown"));
            LogUnknownShipper(logger, shipperId);
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "There is no such Shipper.");
        }

        using var received = logger.BeginScope(EndpointTracking.Outcome("Succeeded", failureCode: null));
        LogReceived(logger, result.Value.Repeated);
        return TypedResults.Ok(result.Value);
    }

    // The schema's rules (shipper-notification.v1.schema.json), so what the endpoint takes is what the format allows.
    private static List<string> Problems(string? correlationId, ShipperNotificationBody body)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            problems.Add($"The request needs an {CorrelationIdHeader}.");
        }

        if (!Guid.TryParse(body.NotificationId, out var id) || id == Guid.Empty)
        {
            problems.Add("The notification needs a notificationId that is a UUID.");
        }

        Require(problems, body.BookingRef, "bookingRef");
        if (body.TransportOrderNumber is null || !TransportOrderNumber().IsMatch(body.TransportOrderNumber))
        {
            problems.Add("The notification needs a transportOrderNumber such as TO-0000042.");
        }

        if (!ShipperExceptionCodes.TryParse(body.ExceptionCode, out _))
        {
            problems.Add($"The notification needs an exceptionCode, one of {string.Join(", ", ShipperExceptionCodes.WireNames)}.");
        }

        Require(problems, body.Message, "message");
        Require(problems, body.Contact, "contact");
        foreach (var (name, value) in new[] { ("eta", body.Eta), ("deliverFrom", body.DeliverFrom), ("deliverUntil", body.DeliverUntil) })
        {
            if (value is not null && !LocalDateTimeText.TryParse(value, out _))
            {
                problems.Add($"The {name} must be a local date and time as dd-MM-yyyy HH:mm.");
            }
        }

        if ((body.DeliverFrom is null) != (body.DeliverUntil is null))
        {
            problems.Add("The deliverFrom and deliverUntil come together.");
        }

        if (body.Eta is not null && body.DeliverFrom is not null)
        {
            problems.Add("An agreed delivery window replaces the eta, so the notification has one or the other.");
        }

        return problems;
    }

    // Only called for a body that passed Problems, so what it holds parses.
    private static DeliveryTiming TimingOf(ShipperNotificationBody body)
    {
        if (LocalDateTimeText.TryParse(body.DeliverFrom, out var from) && LocalDateTimeText.TryParse(body.DeliverUntil, out var until))
        {
            return DeliveryTiming.WithWindow(from, until);
        }

        return LocalDateTimeText.TryParse(body.Eta, out var eta) ? DeliveryTiming.WithEta(eta) : DeliveryTiming.None;
    }

    private static void Require(List<string> problems, string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"The notification needs a {name}.");
        }
    }

    [GeneratedRegex("^TO-[0-9]{7}$")]
    private static partial Regex TransportOrderNumber();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification endpoint of Shipper {ShipperId} is down: answered 503 (fault)")]
    private static partial void LogDown(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused a notification for Shipper {ShipperId}: the credential is missing or wrong")]
    private static partial void LogRefused(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused an invalid notification for Shipper {ShipperId}")]
    private static partial void LogInvalid(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Shipper Persona has the ID {ShipperId}")]
    private static partial void LogUnknownShipper(ILogger logger, string shipperId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Put the notification in the Shipper's inbox (repeated: {Repeated})")]
    private static partial void LogReceived(ILogger logger, bool repeated);
}
