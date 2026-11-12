using Norvia.PartnerPortal.Application.TrackingAssistant;

namespace Norvia.PartnerPortal.Application.UnitTests.TrackingAssistant;

public sealed class AssistantLanguageTests
{
    [Theory]
    [InlineData("Waar is mijn bestelling?", AssistantLanguageKind.Dutch)]
    [InlineData("Wanneer wordt mijn zending geleverd", AssistantLanguageKind.Dutch)]
    [InlineData("Where is my order?", AssistantLanguageKind.English)]
    [InlineData("TO-0000042", AssistantLanguageKind.English)]
    [InlineData("", AssistantLanguageKind.English)]
    public void Detect_TellsDutchFromEnglish_AndDefaultsToEnglish(string question, AssistantLanguageKind expected)
    {
        AssistantLanguage.Detect(question).Should().Be(expected);
    }

    [Theory]
    [InlineData(AssistantLanguageKind.English, "I have no information about that.", "You have asked a lot of questions. Try again in a few minutes.", "Assistant unavailable", "I can't reach tracking right now")]
    [InlineData(AssistantLanguageKind.Dutch, "Daar heb ik geen informatie over.", "U heeft veel vragen gesteld. Probeer het over enkele minuten opnieuw.", "Assistent niet beschikbaar", "Ik kan de tracking nu niet bereiken")]
    public void Texts_AreFixedInBothLanguages(AssistantLanguageKind language, string none, string limited, string unavailable, string tracking)
    {
        AssistantTexts.NoInformation(language).Should().Be(none);
        AssistantTexts.RateLimited(language).Should().Be(limited);
        AssistantTexts.Unavailable(language).Should().Be(unavailable);
        AssistantTexts.TrackingUnavailable(language).Should().Be(tracking);
    }
}
