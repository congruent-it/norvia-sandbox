namespace Norvia.Simulator.Application.Reset;

/// <summary>The applications that keep what a run leaves behind, and each own a reset door (ADR 0039).</summary>
public enum EstateApp
{
    Tms,
    PartnerPortal,
    DriverApp,
}

public static class EstateApps
{
    public static IReadOnlyList<EstateApp> All { get; } = [EstateApp.Tms, EstateApp.PartnerPortal, EstateApp.DriverApp];

    public static string NameOf(EstateApp app) => app switch
    {
        EstateApp.Tms => "TMS",
        EstateApp.PartnerPortal => "Partner portal",
        EstateApp.DriverApp => "Driver app",
        _ => throw new ArgumentOutOfRangeException(nameof(app)),
    };
}
