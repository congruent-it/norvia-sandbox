namespace Norvia.PartnerPortal.Application.TrackingAssistant;

/// <summary>The fixed texts the portal shows in place of anything the model wrote, in the language of the question (ADR 0049).</summary>
public static class AssistantTexts
{
    public static string NoInformation(AssistantLanguageKind language) =>
        language == AssistantLanguageKind.Dutch ? "Daar heb ik geen informatie over." : "I have no information about that.";

    public static string RateLimited(AssistantLanguageKind language) =>
        language == AssistantLanguageKind.Dutch
            ? "U heeft veel vragen gesteld. Probeer het over enkele minuten opnieuw."
            : "You have asked a lot of questions. Try again in a few minutes.";

    public static string Unavailable(AssistantLanguageKind language) =>
        language == AssistantLanguageKind.Dutch ? "Assistent niet beschikbaar" : "Assistant unavailable";

    public static string TrackingUnavailable(AssistantLanguageKind language) =>
        language == AssistantLanguageKind.Dutch ? "Ik kan de tracking nu niet bereiken" : "I can't reach tracking right now";
}
