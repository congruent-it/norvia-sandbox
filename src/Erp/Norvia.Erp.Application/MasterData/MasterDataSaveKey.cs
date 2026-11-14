namespace Norvia.Erp.Application.MasterData;

/// <summary>The sender's Idempotency-Key on a master-data save: the same key on each retry of one save.</summary>
public static class MasterDataSaveKey
{
    public const int MaxLength = 100;

    public const string TooLongCode = "MasterDataKeyTooLong";

    public static bool IsTooLong(string? key) => key is { Length: > MaxLength };

    public static string TooLongMessage => $"The Idempotency-Key is at most {MaxLength} characters.";
}
