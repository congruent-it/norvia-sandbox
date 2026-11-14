namespace Norvia.Erp.Application.MasterData;

/// <summary>What saving a master-data record did. Unchanged means the content was the record's already, so nothing was published.</summary>
public enum MasterDataSaveOutcome
{
    Created,
    Updated,
    Unchanged,

    /// <summary>The same save, under the same Idempotency-Key, was applied before; this one changed and published nothing.</summary>
    Repeat,
}
