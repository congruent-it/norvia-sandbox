namespace Norvia.Agents.UnitTests;

/// <summary>What the cost tools of the health-check API return, as the FinOps agent reads them.</summary>
internal static class FinOpsSamples
{
    public const string CostView = """
        {"periodDays":7,"excludesProvisionalDays":true,"total":{"name":"Total","previousEurPerDay":4.10,"currentEurPerDay":7.40,"change":"+80%"},"transportOrders":{"name":"Transport Orders","previousEurPerDay":null,"currentEurPerDay":null,"previousPerDay":40,"currentPerDay":41,"change":"+3%"},"resources":[{"name":"log-norvia","previousEurPerDay":0.90,"currentEurPerDay":4.20,"change":"+367%"},{"name":"sb-norvia","previousEurPerDay":3.20,"currentEurPerDay":3.20,"change":"0%"}]}
        """;

    public const string RisingSpend = """
        {"estate":"as-found","checksRun":1,"findings":[{"id":"F-COST-RISING-SPEND","checkId":"COST-RISING-SPEND","title":"A resource's spend rises with no more Transport Orders","rule":"No resource's spend rises by half or more while the number of Transport Orders stays flat.","severity":"Medium","evidence":[{"subject":"log-norvia","detail":"Daily spend rose from 0.90 to 4.20 EUR (+367%) over the last 7 days compared with the 7 before, while Transport Orders a day changed by +2.5%."}],"asFoundToggle":"chattyTelemetry","fix":"Set chattyTelemetry to false and provision the estate: the applications then log at Information again."}]}
        """;

    public const string NothingRising = """{"estate":"fixed","checksRun":1,"findings":[]}""";
}
