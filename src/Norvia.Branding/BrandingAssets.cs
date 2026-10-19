namespace Norvia.Branding;

/// <summary>Where the Norvia icons are served from: the static web assets of this library, under the same path in every web UI.</summary>
public static class BrandingAssets
{
    /// <summary>
    /// The path every icon is under. An Easy Auth gate leaves it open (EntraSignIn in the AppHost), so the browser gets the tab icon on
    /// the sign-in redirect too.
    /// </summary>
    public const string Root = "_content/Norvia.Branding";

    public const string Svg = Root + "/favicon.svg";
    public const string Ico = Root + "/favicon.ico";
    public const string AppleTouchIcon = Root + "/apple-touch-icon.png";
}
