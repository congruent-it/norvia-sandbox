namespace Norvia.Tms.Api.TransportOrders;

/// <summary>How Customer service releases a Transport Order without a Proof of delivery: the remark that is required, and who releases it.</summary>
/// <param name="Remark">Required: why, for example "CMR received by post, scanned in the dossier".</param>
/// <param name="ReleasedBy">Required: the Customer service employee who releases it.</param>
public sealed record ReleaseWithoutProofOfDeliveryRequest(string? Remark, string? ReleasedBy);
