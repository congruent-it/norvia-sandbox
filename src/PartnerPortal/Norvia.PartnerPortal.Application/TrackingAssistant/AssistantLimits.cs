namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The limits of the Shipper tracking assistant (ADR 0049), estate parameters whose defaults are the ADR's numbers.</summary>
public sealed record AssistantLimits
{
    public int QuestionsPerMinute { get; init; } = 6;

    public int QuestionsPerHour { get; init; } = 60;

    public int MaxQuestionCharacters { get; init; } = 500;

    public int MaxOrders { get; init; } = 20;

    public int MaxEventsPerOrder { get; init; } = 20;

    public int MaxOutputTokens { get; init; } = 400;

    public int DailyTokensPerPersona { get; init; } = 50_000;

    public int DailyTokensEstate { get; init; } = 500_000;
}
