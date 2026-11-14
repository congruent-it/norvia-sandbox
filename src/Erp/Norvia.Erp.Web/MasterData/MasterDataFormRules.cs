using Norvia.Erp.Application.MasterData.Shippers.Save;

namespace Norvia.Erp.Web.MasterData;

/// <summary>
/// What Finance's master-data forms check before they send a save, so a mistake is said beside the field. The ERP API checks the same
/// again; a new record's ID and a Vehicle's registration being unique is checked only here, against what the ERP holds.
/// </summary>
internal static class MasterDataFormRules
{
    public const int MaxIdLength = SaveShipperHandler.MaxIdLength;
    public const int MaxNameLength = SaveShipperHandler.MaxNameLength;

    /// <summary>The ID of a new record is required, short enough and not one the ERP already holds. An edit keeps its ID.</summary>
    public static string? CheckNewId(string kind, string? id, bool editing, IReadOnlyCollection<string> taken)
    {
        if (editing)
        {
            return null;
        }

        var trimmed = id?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return $"The {kind} needs an ID.";
        }

        if (trimmed.Length > MaxIdLength)
        {
            return $"The ID is at most {MaxIdLength} characters.";
        }

        return taken.Contains(trimmed, StringComparer.OrdinalIgnoreCase) ? $"There is already a {kind} with ID {trimmed}." : null;
    }

    public static string? CheckName(string kind, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"The {kind} needs a name.";
        }

        return name.Trim().Length > MaxNameLength ? $"The name is at most {MaxNameLength} characters." : null;
    }
}
