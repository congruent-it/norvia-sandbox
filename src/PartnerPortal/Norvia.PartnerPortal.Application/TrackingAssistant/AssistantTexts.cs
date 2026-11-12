namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The fixed texts the portal shows in place of anything the model wrote, in the language of the question (ADR 0049).</summary>
public static class AssistantTexts
{
    private sealed record Words(string NoInformation, string RateLimited, string Unavailable, string TrackingUnavailable);

    private static readonly Words English = new(
        "I have no information about that.",
        "You have asked a lot of questions. Try again in a few minutes.",
        "Assistant unavailable",
        "I can't reach tracking right now");

    private static readonly Words Dutch = new(
        "Daar heb ik geen informatie over.",
        "U heeft veel vragen gesteld. Probeer het over enkele minuten opnieuw.",
        "Assistent niet beschikbaar",
        "Ik kan de tracking nu niet bereiken");

    public static string NoInformation(AssistantLanguageKind language) => In(language).NoInformation;

    public static string RateLimited(AssistantLanguageKind language) => In(language).RateLimited;

    public static string Unavailable(AssistantLanguageKind language) => In(language).Unavailable;

    public static string TrackingUnavailable(AssistantLanguageKind language) => In(language).TrackingUnavailable;

    private static Words In(AssistantLanguageKind language) => language == AssistantLanguageKind.Dutch ? Dutch : English;
}
