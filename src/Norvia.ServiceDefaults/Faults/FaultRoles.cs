namespace Norvia.ServiceDefaults.Faults;

public static class FaultRoles
{
    /// <summary>The app role that guards every fault switch. Whoever lacks it gets a 403, and a caller without a token a 401.</summary>
    public const string DemoFault = "Demo.Fault";
}
