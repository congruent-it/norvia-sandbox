using Norvia.DriverApp.Application.Reports.Deliver;

namespace Norvia.DriverApp.Api.Actions;

/// <param name="Content">The file's bytes, base64 in JSON.</param>
public sealed record ProofFileRequest(string ContentType, byte[] Content)
{
    public ProofFile ToFile() => new(ContentType, Content);
}
