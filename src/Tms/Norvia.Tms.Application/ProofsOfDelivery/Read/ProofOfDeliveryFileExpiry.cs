using Ardalis.Result;

namespace Norvia.Tms.Application.ProofsOfDelivery.Read;

/// <summary>
/// Tells a Proof of delivery file the retention period has deleted from one that never existed. The Proof of delivery keeps its
/// metadata and file list after the storage deletes the content, so such a file is known but gone: a NotFound that carries
/// <see cref="Marker"/>, which the callers show as expired and not as an error.
/// </summary>
public static class ProofOfDeliveryFileExpiry
{
    public const string Marker = "ProofOfDeliveryFileExpired";

    public static Result<ProofOfDeliveryFileContent> Expired() => Result<ProofOfDeliveryFileContent>.NotFound(Marker);

    public static bool IsExpired(this Result<ProofOfDeliveryFileContent> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Status == ResultStatus.NotFound && result.Errors.Contains(Marker);
    }
}
