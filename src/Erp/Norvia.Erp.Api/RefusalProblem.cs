using Ardalis.Result;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Norvia.Erp.Api;

/// <summary>
/// The problem every operation answers a refused call with: a status that says which rule it broke, the refusal's own
/// stable code where it has one, and the call's own correlation ID, which the refusal is traced on, so the callers can
/// join its Message trail. What differs per operation is only which status each of its refusal codes gets.
/// </summary>
internal static class RefusalProblem
{
    /// <param name="statusOf">The status of each of the operation's refusal codes.</param>
    /// <param name="statusWithoutCode">
    /// The status of a refusal that has no code, for an operation with such a rule (a Carrier answering another
    /// Carrier's booking); null when every refusal of the operation has one.
    /// </param>
    public static ProblemHttpResult Of<TCode, TResult>(
        Result<TResult> result, string title, string correlationId, Func<TCode, int> statusOf, int? statusWithoutCode = null)
        where TCode : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(statusOf);
        var refusal = result.ValidationErrors.First();
        var extensions = new Dictionary<string, object?> { ["correlationId"] = correlationId };
        int status;
        if (string.IsNullOrEmpty(refusal.ErrorCode))
        {
            status = statusWithoutCode ?? throw new InvalidOperationException($"A refusal of this operation has no code: {refusal.ErrorMessage}");
        }
        else
        {
            extensions["code"] = refusal.ErrorCode;
            status = statusOf(Enum.Parse<TCode>(refusal.ErrorCode));
        }

        return TypedResults.Problem(statusCode: status, title: title, detail: refusal.ErrorMessage, extensions: extensions);
    }
}
