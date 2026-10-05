using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ardalis.Result;
using Mediator;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norvia.Tms.Api.Tracking;
using Norvia.Tms.Api.TransportOrders;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Application.ProofsOfDelivery.Read;
using Norvia.Tms.Application.ProofsOfDelivery.Record;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.ProofsOfDelivery;
using Norvia.Tms.Domain.TrackingEvents;

namespace Norvia.Tms.Api.ProofsOfDelivery;

public static class ProofOfDeliveryEndpoints
{
    /// <summary>
    /// The app role of the callers that record Proofs of delivery: the Carrier Proof of delivery Logic App's managed
    /// identity, and later P12's Driver reports Logic App's.
    /// </summary>
    public const string RecordRole = "ProofsOfDelivery.Record";

    private const string MetadataPart = "metadata";
    private const string FilesPart = "files";

    public static IEndpointRouteBuilder MapProofsOfDelivery(this IEndpointRouteBuilder app)
    {
        var proofsOfDelivery = app.NewVersionedApi("ProofsOfDelivery")
            .MapGroup("/v{version:apiVersion}")
            .HasApiVersion(1);

        // The call is multipart, which the framework only binds with an antiforgery token a Logic App has no way to
        // send. The token check of the role is what guards it.
        proofsOfDelivery.MapPost("/proofs-of-delivery", RecordAsync)
            .DisableAntiforgery()
            .WithName("RecordProofOfDelivery")
            .RequireAuthorization(policy => policy.RequireRole(RecordRole))
            .WithMetadata(new TrackedFlow(Flows.CarrierProofOfDelivery))
            .Accepts<ProofOfDeliveryRequest>("multipart/form-data")
            .WithSummary("Record a Proof of delivery a Carrier sent for a Shipment, with its files.")
            .WithDescription(
                "Multipart: the canonical metadata as JSON in the 'metadata' part and each file in a 'files' part. The metadata names its "
                + "source: a Carrier ID (from API Management, never from the Carrier's payload) or Own fleet. The Shipment is the one on that "
                + "Carrier's Confirmed Carrier booking with the Carrier booking reference. The TMS keeps the files in its own storage, records "
                + "the Proof of delivery as the Shipment's current one with the correlation ID, and publishes ProofOfDeliveryReceived in the "
                + "same transaction as the record. It never changes the Shipment's status. A Proof of delivery is refused, changing and "
                + "publishing nothing, with a problem whose code says why: ShipmentNotFound (404), ShipmentNotInTransport (409), "
                + "MissingRequiredField (400); and 400 for an input that isn't the Canonical Model.");

        proofsOfDelivery.MapGet("/shipments/{shipmentNumber}/proof-of-delivery", GetAsync)
            .RequireAuthorization()
            .WithName("GetShipmentProofOfDelivery")
            .WithSummary("Read a Shipment's current Proof of delivery: who signed, when and where, the condition and its files.");

        proofsOfDelivery.MapGet("/shipments/{shipmentNumber}/proof-of-delivery/files/{fileId:guid}", GetFileAsync)
            .RequireAuthorization()
            .WithName("GetShipmentProofOfDeliveryFile")
            .WithSummary("Read one file of a Shipment's current Proof of delivery, with the content type it was sent with.");

        return app;
    }

    private static async Task<Results<Ok<RecordProofOfDeliveryResult>, ProblemHttpResult>> RecordAsync(
        [FromHeader(Name = TransportOrderEndpoints.CorrelationIdHeader)] string? correlationId,
        HttpRequest request,
        IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Invalid([Missing($"Recording a Proof of delivery needs an {TransportOrderEndpoints.CorrelationIdHeader}.")], string.Empty);
        }

        if (!request.HasFormContentType)
        {
            return Invalid([Missing("A Proof of delivery is sent as multipart form data: its metadata and its files.")], correlationId);
        }

        var form = await request.ReadFormAsync(cancellationToken);
        if (!TryReadMetadata(form, json.Value.SerializerOptions, out var metadata, out var metadataProblem))
        {
            return Invalid([metadataProblem], correlationId);
        }

        var contents = new List<ReadOnlyMemory<byte>>();
        var described = new List<ProofOfDeliveryFile>();
        foreach (var file in form.Files.Where(file => file.Name == FilesPart))
        {
            using var content = new MemoryStream();
            await file.CopyToAsync(content, cancellationToken);
            contents.Add(content.ToArray());
            described.Add(ProofOfDeliveryFile.New(file.FileName, file.ContentType, file.Length));
        }

        if (!TryRead(metadata, described, out var report, out var problems))
        {
            return Invalid(problems, correlationId);
        }

        var result = await mediator.Send(new RecordProofOfDeliveryCommand(report, contents, correlationId), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : RefusalProblem.Of<ProofOfDeliveryRefusalCode, RecordProofOfDeliveryResult>(result, "The Proof of delivery can't be recorded.", correlationId, StatusCodeOf);
    }

    private static async Task<Results<Ok<ProofOfDeliveryView>, ProblemHttpResult>> GetAsync(
        string shipmentNumber,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetProofOfDeliveryQuery(shipmentNumber), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} has no Proof of delivery.")
            : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> GetFileAsync(
        string shipmentNumber,
        Guid fileId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetProofOfDeliveryFileQuery(shipmentNumber, fileId), cancellationToken);
        return result.Status == ResultStatus.NotFound
            ? TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: $"Shipment {shipmentNumber} has no such Proof of delivery file.")
            : TypedResults.File(result.Value.Content, result.Value.ContentType, result.Value.Name);
    }

    /// <summary>A 400 for a Proof of delivery the TMS can't read. Its <c>code</c> is the first problem's, and <c>errors</c> lists every one.</summary>
    private static ProblemHttpResult Invalid(IReadOnlyList<ReadProblem> problems, string correlationId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The Proof of delivery can't be recorded.",
            detail: problems[0].Message,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = problems[0].Code.ToString(),
                ["correlationId"] = correlationId,
                ["errors"] = problems.Select(problem => new { code = problem.Code.ToString(), message = problem.Message }).ToArray(),
            });

    private static ReadProblem Missing(string message) => new(ProofOfDeliveryRefusalCode.MissingRequiredField, message);

    /// <summary>The metadata part: JSON in the Canonical Model, as a form field or as a file part.</summary>
    private static bool TryReadMetadata(
        IFormCollection form,
        JsonSerializerOptions options,
        [NotNullWhen(true)] out ProofOfDeliveryRequest? metadata,
        out ReadProblem problem)
    {
        metadata = null;
        problem = Missing("A Proof of delivery needs its metadata part, as Canonical Model JSON.");
        string? text = form.TryGetValue(MetadataPart, out var field) ? field.ToString() : null;
        if (text is null && form.Files.GetFile(MetadataPart) is { } part)
        {
            using var reader = new StreamReader(part.OpenReadStream());
            text = reader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            metadata = JsonSerializer.Deserialize<ProofOfDeliveryRequest>(text, options);
        }
        catch (JsonException ex)
        {
            problem = Missing($"The metadata part isn't the Canonical Model: {ex.Message}");
            return false;
        }

        return metadata is not null;
    }

    /// <summary>
    /// Reads the Canonical Model metadata as the domain takes it, beyond its JSON shape: the fields it requires, and a
    /// condition the TMS knows. Either returns the report, or every problem found, in the order of the metadata's fields.
    /// </summary>
    private static bool TryRead(
        ProofOfDeliveryRequest metadata,
        List<ProofOfDeliveryFile> files,
        [NotNullWhen(true)] out ProofOfDeliveryReport? report,
        out IReadOnlyList<ReadProblem> problems)
    {
        var found = new List<ReadProblem>();
        report = null;
        problems = found;

        if (string.IsNullOrWhiteSpace(metadata.DocumentId))
        {
            found.Add(Missing("A Proof of delivery needs its source's own document ID."));
        }

        ProofOfDeliverySource? source = null;
        if (metadata.Source is not { } sourceRequest)
        {
            found.Add(Missing("A Proof of delivery needs its source."));
        }
        else if (sourceRequest.Type == ProofOfDeliverySourceType.Carrier)
        {
            if (string.IsNullOrWhiteSpace(sourceRequest.CarrierId))
            {
                found.Add(Missing("A Carrier source needs the Carrier ID."));
            }
            else
            {
                source = ProofOfDeliverySource.Carrier(new CarrierId(sourceRequest.CarrierId));
            }
        }
        else if (sourceRequest.CarrierId is not null)
        {
            found.Add(Missing("An Own fleet source names no Carrier."));
        }
        else
        {
            source = ProofOfDeliverySource.OwnFleet;
        }

        if (string.IsNullOrWhiteSpace(metadata.CarrierBookingReference))
        {
            found.Add(Missing("A Proof of delivery needs the Carrier's own booking reference."));
        }

        if (string.IsNullOrWhiteSpace(metadata.ReceivedBy))
        {
            found.Add(Missing("A Proof of delivery needs the name of the person who signed."));
        }

        if (metadata.DeliveryTime is null)
        {
            found.Add(Missing("A Proof of delivery needs the time the goods were delivered."));
        }

        TrackingLocation? place = null;
        if (metadata.Location is not { } location || string.IsNullOrWhiteSpace(location.Place) || string.IsNullOrWhiteSpace(location.Postcode))
        {
            found.Add(Missing("A location needs its place and postcode."));
        }
        else if (location.CountryCode is not { Length: 2 } || !location.CountryCode.All(char.IsAsciiLetterUpper))
        {
            found.Add(Missing("A location needs its country as an ISO 3166-1 alpha-2 code."));
        }
        else
        {
            place = new TrackingLocation(location.Place, location.Postcode, location.CountryCode);
        }

        DeliveryCondition? condition = null;
        if (string.IsNullOrWhiteSpace(metadata.Condition))
        {
            found.Add(Missing("A Proof of delivery needs its condition."));
        }
        else if (Enum.TryParse<DeliveryCondition>(metadata.Condition, out var parsed) && Enum.IsDefined(parsed) && !metadata.Condition.All(char.IsAsciiDigit))
        {
            condition = parsed;
        }
        else
        {
            found.Add(Missing($"'{metadata.Condition}' isn't a condition the TMS knows: Clean or Reservations."));
        }

        if (files.Count == 0)
        {
            found.Add(Missing("A Proof of delivery needs at least one file, in a 'files' part."));
        }

        if (found.Count > 0
            || metadata.DocumentId is not { } documentId
            || source is null
            || metadata.CarrierBookingReference is not { } reference
            || metadata.ReceivedBy is not { } receivedBy
            || metadata.DeliveryTime is not { } deliveryTime
            || place is null
            || condition is not { } knownCondition)
        {
            return false;
        }

        report = new ProofOfDeliveryReport(source, documentId, reference, receivedBy, deliveryTime, place, knownCondition, metadata.Remark, files);
        return true;
    }

    /// <summary>One thing wrong with a Proof of delivery the TMS can't read, with the stable code of its kind.</summary>
    private readonly record struct ReadProblem(ProofOfDeliveryRefusalCode Code, string Message);

    private static int StatusCodeOf(ProofOfDeliveryRefusalCode code) => code switch
    {
        ProofOfDeliveryRefusalCode.ShipmentNotFound => StatusCodes.Status404NotFound,
        ProofOfDeliveryRefusalCode.MissingRequiredField => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict, // ShipmentNotInTransport
    };
}
