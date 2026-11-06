using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Norvia.Agents.Scripts;

/// <summary>
/// The scripted model of the Simulator's personas agent (ADR 0041): for a language and a category it gives a fixed list of texts, so a
/// local run and CI are deterministic and need no Azure. It makes no tool call. A question it can't read gets an answer that isn't a list
/// of texts, which the runner refuses as an unavailable Agent, as it would a model that lost its way.
/// </summary>
public static class PersonaTextScript
{
    public const string DeclineReason = "DeclineReason";

    public const string DeliveryFailedRemark = "DeliveryFailedRemark";

    private static readonly Dictionary<(string Language, string Category), string[]> Texts = new()
    {
        [("en", DeclineReason)] =
        [
            "Regrettably our fleet is fully committed on that date",
            "We are unable to accept this booking at the requested time",
            "Our vehicles are not available for that collection window",
            "I am afraid we cannot cover this lane at present",
            "Capacity is exhausted for the day in question",
        ],
        [("en", DeliveryFailedRemark)] =
        [
            "The consignee was not present to receive the goods",
            "Access to the unloading dock was refused by the site",
            "Delivery could not be completed before the site closed",
            "The consignee declined to sign for the shipment",
            "No one was available at the premises to accept delivery",
        ],
        [("nl", DeclineReason)] =
        [
            "Geen wagen vrij op die dag",
            "Vol, kunnen niet meer aannemen",
            "Past niet in onze planning",
            "Geen chauffeur beschikbaar",
            "Rit niet haalbaar, sorry",
        ],
        [("nl", DeliveryFailedRemark)] =
        [
            "Niemand thuis bij aflevering",
            "Ontvanger niet aanwezig",
            "Dock dicht, niet kunnen lossen",
            "Ontvanger weigert handtekening",
            "Adres gesloten bij aankomst",
        ],
        [("mixed", DeclineReason)] =
        [
            "sorry geen capacity die dag, truck is stuk",
            "kunnen we niet doen, no driver beschikbaar",
            "vol!! maybe volgende week",
            "geen plek meer in de wagen sorry",
            "too far, past niet in de route",
        ],
        [("mixed", DeliveryFailedRemark)] =
        [
            "niemand there, gebeld maar geen antwoord",
            "receiver not home, kaartje achtergelaten",
            "dock was closed al, kon niet lossen",
            "klant refused to sign",
            "adres klopt niet?? niemand open gedaan",
        ],
        [("de", DeclineReason)] =
        [
            "Leider keine freie Kapazität an diesem Tag",
            "Dieses Zeitfenster können wir nicht bedienen",
            "Fahrzeug an diesem Tag nicht verfügbar",
            "Diese Strecke fahren wir derzeit nicht",
            "Auslastung zu hoch, Auftrag nicht annehmbar",
        ],
        [("de", DeliveryFailedRemark)] =
        [
            "Empfänger bei der Zustellung nicht angetroffen",
            "Anlieferung abgelehnt, Annahme geschlossen",
            "Entladung vor Ort nicht möglich",
            "Empfänger verweigert die Annahme",
            "Niemand zur Warenannahme anwesend",
        ],
    };

    public static IChatClient Model() => new ScriptedChatClient(Next);

    private static IReadOnlyList<AIContent> Next(ScriptContext context)
    {
        if (!PersonaQuestions.TryParse(context.Question, out var language, out var category) || !Texts.TryGetValue((language, category), out var texts))
        {
            return [new TextContent("{}")];
        }

        return [new TextContent(JsonSerializer.Serialize(new { texts }))];
    }
}
