namespace Norvia.HealthCheck.Web;

/// <summary>What the browser's tab says. The app's name comes first, so a narrow tab still tells the Estate health check apart from the other Norvia apps.</summary>
public static class PageTitles
{
    public const string App = "Estate health check";

    public const string Run = App;

    public const string Refused = $"{App} · Not allowed";
}
