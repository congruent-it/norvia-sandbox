using System.ComponentModel.DataAnnotations;

namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The limits of the Shipper tracking assistant (ADR 0049), estate parameters whose defaults are the ADR's numbers.</summary>
public sealed record AssistantLimits
{
    /// <summary>The default size of a question, which the panel's input holds to as well.</summary>
    public const int DefaultMaxQuestionCharacters = 500;

    [Range(1, int.MaxValue)]
    public int QuestionsPerMinute { get; init; } = 6;

    [Range(1, int.MaxValue)]
    public int QuestionsPerHour { get; init; } = 60;

    [Range(1, int.MaxValue)]
    public int MaxQuestionCharacters { get; init; } = DefaultMaxQuestionCharacters;

    [Range(1, int.MaxValue)]
    public int MaxOrders { get; init; } = 20;

    [Range(1, int.MaxValue)]
    public int MaxEventsPerOrder { get; init; } = 20;

    [Range(1, int.MaxValue)]
    public int MaxOutputTokens { get; init; } = 400;

    [Range(1, int.MaxValue)]
    public int DailyTokensPerPersona { get; init; } = 50_000;

    [Range(1, int.MaxValue)]
    public int DailyTokensEstate { get; init; } = 500_000;
}
