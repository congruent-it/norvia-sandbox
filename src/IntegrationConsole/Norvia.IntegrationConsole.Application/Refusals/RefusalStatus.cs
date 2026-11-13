namespace Norvia.IntegrationConsole.Application.Refusals;

/// <summary>What API Management refuses at the edge with, and what a refusal is called when its source doesn't name something.</summary>
public static class RefusalStatus
{
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int TooManyRequests = 429;

    /// <summary>The operation or failure code of a refusal whose source records none.</summary>
    public const string Unknown = "unknown";

    public static IReadOnlyList<int> All { get; } = [Unauthorized, Forbidden, TooManyRequests];
}
