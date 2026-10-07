namespace Norvia.Integration.LocalPartnerApi;

/// <summary>What the TMS answered: its status, its body as it came, and the headers a file keeps.</summary>
internal sealed record TmsAnswer(int StatusCode, byte[] Content, string? ContentType, string? ContentDisposition)
{
    /// <summary>The TMS didn't answer: it couldn't be reached, or didn't answer in time.</summary>
    public static TmsAnswer Unreachable { get; } = new(0, [], null, null);
}
