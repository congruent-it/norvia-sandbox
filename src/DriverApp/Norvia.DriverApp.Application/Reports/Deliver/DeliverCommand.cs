using Ardalis.Result;
using Mediator;
using Norvia.DriverApp.Domain.Reports;

namespace Norvia.DriverApp.Application.Reports.Deliver;

/// <summary>The Driver delivers a delivery Stop with a Proof of delivery (P12).</summary>
/// <param name="DriverId">The Driver ID from the Driver's token, never from the request (ADR 0005, ADR 0037).</param>
/// <param name="ReceivedBy">The name of the person who signed.</param>
/// <param name="Remark">What the Driver says of the reservations. Required with reservations, and dropped otherwise.</param>
/// <param name="Signature">The signature drawn on screen, as a PNG.</param>
/// <param name="Photos">0 to 4 photos, JPEG or PNG.</param>
public sealed record DeliverCommand(
    string TripNumber,
    string DriverId,
    string ShipmentNumber,
    string? ReceivedBy,
    DeliveryConditionDto? Condition,
    string? Remark,
    ProofFile? Signature,
    IReadOnlyList<ProofFile> Photos) : ICommand<Result<DeliverResult>>
{
    public const int MaximumPhotos = ProofOfDeliveryRules.MaximumPhotos;

    public const long MaximumFileBytes = ProofOfDeliveryRules.MaximumFileBytes;

    public const int MaximumRemarkLength = StopReportRules.MaximumRemarkLength;

    public const int MaximumReceivedByLength = ProofOfDeliveryRules.MaximumReceivedByLength;
}
