namespace Norvia.Erp.Application.MasterData;

/// <summary>What saving a master-data record did. Unchanged means the content was the record's already, so nothing was published.</summary>
public enum MasterDataSaveOutcome
{
    Created,
    Updated,
    Unchanged,
}
