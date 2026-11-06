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

    public const string DriverCollectionFailedRemark = "DriverCollectionFailedRemark";

    public const string DriverDeliveryFailedRemark = "DriverDeliveryFailedRemark";

    public const string DriverDamageRemark = "DriverDamageRemark";

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
        [("en", DriverCollectionFailedRemark)] =
        [
            "Nobody was at the collection address to hand over the goods",
            "The site refused entry and I could not load",
            "The goods were not ready for collection at the agreed time",
            "The shipper's dock was closed on arrival",
            "The sender could not be reached and the goods stayed behind",
        ],
        [("en", DriverDeliveryFailedRemark)] =
        [
            "The consignee was not present to receive the goods",
            "Delivery was refused because the site had already closed",
            "Nobody at the address could sign for the shipment",
            "The unloading bay was blocked and I could not deliver",
            "The consignee declined the goods at the door",
        ],
        [("en", DriverDamageRemark)] =
        [
            "One pallet arrived damaged at the pickup",
            "Packaging was torn open when I collected the goods",
            "A carton was crushed before loading",
            "Visible water damage on two pallets at collection",
            "The shrink wrap had split and the load was loose",
        ],
        [("nl", DriverCollectionFailedRemark)] =
        [
            "Niemand aanwezig bij het ophaaladres",
            "Terrein geweigerd, niet kunnen laden",
            "Goederen niet klaar",
            "Dock dicht bij aankomst",
            "Afzender niet bereikbaar",
        ],
        [("nl", DriverDeliveryFailedRemark)] =
        [
            "Ontvanger niet aanwezig",
            "Niemand thuis, kon niet afleveren",
            "Dock dicht bij aankomst",
            "Ontvanger weigert de lading",
            "Niemand wil tekenen",
        ],
        [("nl", DriverDamageRemark)] =
        [
            "Pallet beschadigd bij ophalen",
            "Verpakking kapot bij het laden",
            "Doos ingedrukt",
            "Waterschade op twee pallets",
            "Folie los, lading loslopend",
        ],
        [("mixed", DriverCollectionFailedRemark)] =
        [
            "niemand there bij ophaaladres",
            "kon niet laden, gate was closed",
            "goederen nog niet klaar sorry",
            "dock dicht bij aankomst",
            "afzender not reachable, gebeld",
        ],
        [("mixed", DriverDeliveryFailedRemark)] =
        [
            "receiver not home, niemand open gedaan",
            "dock was closed al, kon niet lossen",
            "klant refused to sign",
            "adres klopt niet?? niemand there",
            "wachtte 30 min maar niemand kwam",
        ],
        [("mixed", DriverDamageRemark)] =
        [
            "pallet kapot bij pickup",
            "doos crushed bij het laden",
            "water damage op 2 pallets",
            "folie los en lading loose",
            "hoekje van pallet broken",
        ],
        [("de", DriverCollectionFailedRemark)] =
        [
            "Niemand an der Abholadresse angetroffen",
            "Zutritt verweigert, Beladung nicht möglich",
            "Ware war zum vereinbarten Zeitpunkt nicht bereit",
            "Rampe bei Ankunft geschlossen",
            "Absender nicht erreichbar",
        ],
        [("de", DriverDeliveryFailedRemark)] =
        [
            "Empfänger bei der Zustellung nicht angetroffen",
            "Annahme geschlossen, Zustellung nicht möglich",
            "Niemand konnte die Sendung quittieren",
            "Empfänger verweigert die Annahme",
            "Entladestelle war versperrt",
        ],
        [("de", DriverDamageRemark)] =
        [
            "Eine Palette bei der Abholung beschädigt",
            "Verpackung beim Beladen aufgerissen",
            "Karton eingedrückt vor der Verladung",
            "Wasserschaden an zwei Paletten",
            "Folie gerissen, Ladung lose",
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
