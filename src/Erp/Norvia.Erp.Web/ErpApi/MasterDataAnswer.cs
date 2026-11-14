namespace Norvia.Erp.Web.ErpApi;

/// <summary>What the ERP API did with a save.</summary>
public enum MasterDataOutcome
{
    Created,
    Updated,

    /// <summary>The record already was what Finance saved: nothing changed and nothing was published.</summary>
    Unchanged,
}

/// <summary>The ERP API's answer to a Finance save.</summary>
public abstract record MasterDataAnswer
{
    /// <summary>The record was saved, or already was as saved, at this Master-data revision.</summary>
    public sealed record Saved(MasterDataOutcome Outcome, int Revision) : MasterDataAnswer;

    /// <summary>The ERP API refused the save, and the reason says why.</summary>
    public sealed record Refused(string Reason) : MasterDataAnswer;
}
