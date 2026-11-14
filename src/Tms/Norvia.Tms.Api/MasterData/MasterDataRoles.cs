namespace Norvia.Tms.Api.MasterData;

public static class MasterDataRoles
{
    /// <summary>The app role only the E3 master-data sync Logic App's managed identity holds, so nothing bypasses the integration layer. One role covers every replicated entity.</summary>
    public const string Replace = "MasterData.Replace";
}
