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
    /// identity, and P12's Driver reports Logic App's.
    /// </summary>
    public const string RecordRole = "ProofsOfDelivery.Record";

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
            .WithSummary("Record a Proof of delivery a Carrier or a Driver sent for a Shipment, with its files.")
            .WithDescription(
                "Multipart: the canonical metadata as JSON in the 'metadata' part and each file in a 'files' part. The metadata names its "
                + "source: a Carrier ID (from API Management, never from the Carrier's payload) or Own fleet with the Driver ID (from the Driver's token, "
                + "which the Driver reports Logic App passes on, never from the Driver app's payload). A Carrier's Shipment is the one on that "
                + "Carrier's Confirmed Carrier booking with the Carrier booking reference. An Own fleet Proof of delivery (P12) names its Trip, the Shipment on it "
                + "and the version of the Trip the Driver app held (kept for tracing only) in place of a booking reference, and the X-Correlation-ID is the one "
                + "of the Delivered Tracking event it belongs to. The TMS keeps the files in its own storage, records "
                + "the Proof of delivery as the Shipment's current one with the correlation ID, and publishes ProofOfDeliveryReceived in the "
                + "same transaction as the record. A document ID the source already recorded is a repeat: the answer is the original result with "
                + "isRepeat set, and nothing is recorded or published. A new document ID for a Shipment with a current Proof of delivery makes the "
                + "new one current and supersedes the earlier, with nothing merged; the event says it replaces an earlier one. It never changes the Shipment's status. A Proof of delivery is refused, changing and "
                + "publishing nothing, with a problem whose code says why: ShipmentNotFound (404), ShipmentNotInTransport (409), "
                + "MissingRequiredField (400), DeliveryTimeInTheFuture (400), FileNotAccepted (400); and for an Own fleet Proof of delivery TripNotFound (404, also for "
                + "another Driver's Trip), TripNotUnderWay, ShipmentNotOnTrip (checked against the Trip's latest version, even when the report names an older one) or "
                + "CollectionBeforeDelivery (409); and 400 for an input that isn't the Canonical Model.");

        proofsOfDelivery.MapGet("/shipments/{shipmentNumber}/proof-of-delivery", GetAsync)
            .RequireAuthorization()
            .WithName("GetShipmentProofOfDelivery")
            .WithSummary("Read a Shipment's current Proof of delivery: who signed, when and where, the condition and its files.");

        proofsOfDelivery.MapGet("/shipments/{shipmentNumber}/proof-of-delivery/files/{fileId:guid}", GetFileAsync)
            .RequireAuthorization()
            .WithName("GetShipmentProofOfDeliveryFile")
            .WithSummary("Read one file of a Shipment's Proofs of delivery, current or superseded, with the content type it was sent with.");

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
            return Invalid([ProofOfDeliveryReader.Missing($"Recording a Proof of delivery needs an {TransportOrderEndpoints.CorrelationIdHeader}.")], string.Empty);
        }

        if (!request.HasFormContentType)
        {
            return Invalid([ProofOfDeliveryReader.Missing("A Proof of delivery is sent as multipart form data: its metadata and its files.")], correlationId);
        }

        var form = await request.ReadFormAsync(cancellationToken);
        if (!ProofOfDeliveryReader.TryReadMetadata(form, json.Value.SerializerOptions, out var metadata, out var metadataProblem))
        {
            return Invalid([metadataProblem], correlationId);
        }

        var contents = new List<ReadOnlyMemory<byte>>();
        var described = new List<ProofOfDeliveryFile>();
        foreach (var file in form.Files.Where(file => file.Name == ProofOfDeliveryReader.FilesPart))
        {
            // A file the TMS will refuse for its count or size is described but never buffered: the refusal needs only its size.
            if (described.Count >= ProofOfDeliveryRules.MaxFiles || file.Length > ProofOfDeliveryRules.MaxFileBytes)
            {
                contents.Add(ReadOnlyMemory<byte>.Empty);
            }
            else
            {
                using var content = new MemoryStream();
                await file.CopyToAsync(content, cancellationToken);
                contents.Add(content.ToArray());
            }

            described.Add(ProofOfDeliveryFile.New(file.FileName, file.ContentType, file.Length));
        }

        if (!ProofOfDeliveryReader.TryRead(metadata, described, out var report, out var problems))
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
    private static ProblemHttpResult Invalid(IReadOnlyList<ProofOfDeliveryReader.ReadProblem> problems, string correlationId) =>
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

    private static int StatusCodeOf(ProofOfDeliveryRefusalCode code) => code switch
    {
        ProofOfDeliveryRefusalCode.ShipmentNotFound or ProofOfDeliveryRefusalCode.TripNotFound => StatusCodes.Status404NotFound,
        ProofOfDeliveryRefusalCode.MissingRequiredField or ProofOfDeliveryRefusalCode.DeliveryTimeInTheFuture or ProofOfDeliveryRefusalCode.FileNotAccepted => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status409Conflict, // ShipmentNotInTransport, and the Trip rules of an Own fleet Proof of delivery
    };
}
