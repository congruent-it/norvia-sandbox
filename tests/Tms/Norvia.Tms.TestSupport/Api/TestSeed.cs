using System.Text.Json;
using System.Text.Json.Nodes;

namespace Norvia.Tms.TestSupport;

public static class TestSeed
{
    private static readonly string[] Weekend = ["Saturday", "Sunday"];

    /// <summary>
    /// Writes the test seed to the path with a shift at the weekend for the given Drivers, so a Trip can be planned for them on any day the tests run on. The Drivers' rosters are otherwise Monday to Friday.
    /// </summary>
    public static void WriteWithWeekendShifts(string path, params string[] driverIds)
    {
        var seed = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Seed", "tms.test-seed.json")))!.AsObject();
        foreach (var driver in seed["drivers"]!.AsArray().Where(candidate => driverIds.Contains(candidate!["id"]!.GetValue<string>())))
        {
            var roster = driver!["roster"]!.AsObject();
            foreach (var weekend in Weekend.Where(day => !roster.ContainsKey(day)))
            {
                roster[weekend] = new JsonObject { ["start"] = "08:00", ["end"] = "12:00" };
            }
        }

        File.WriteAllText(path, seed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
