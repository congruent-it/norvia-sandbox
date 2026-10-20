namespace Norvia.IntegrationConsole.Web;

/// <summary>What the browser's tab says. The app's name comes first, so a narrow tab still tells the console apart from the other Norvia apps.</summary>
public static class PageTitles
{
    private const int ShortLength = 8;

    /// <summary>The start of an ID, enough to tell two tabs of the same page apart.</summary>
    public static string ShortId(string? id) => string.IsNullOrWhiteSpace(id) ? string.Empty : id.Length <= ShortLength ? id : id[..ShortLength];
}
