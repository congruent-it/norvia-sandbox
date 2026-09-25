namespace Norvia.Integration.LocalPartnerApi;

/// <summary>
/// The outcome of the edge's checks: the Shipper to book for, or the refusal. A 401 carries API Management's own error;
/// a 403 carries the policy's text, with the correlation ID.
/// </summary>
internal sealed record EdgeCheck(string? ShipperId, int RefusalStatus, string? RefusalText, string? FailureCode)
{
    public static EdgeCheck Passed(string shipperId) => new(shipperId, 0, null, null);

    public static EdgeCheck Unauthorized(string message) => new(null, StatusCodes.Status401Unauthorized, message, "Unauthorized");

    public static EdgeCheck Forbidden(string text, string failureCode) => new(null, StatusCodes.Status403Forbidden, text, failureCode);
}
