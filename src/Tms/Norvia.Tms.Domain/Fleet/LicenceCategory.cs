namespace Norvia.Tms.Domain.Fleet;

/// <summary>
/// A driving licence category. The values run from the lowest to the highest, so a higher category covers a lower one
/// (see <see cref="LicenceRules"/>).
/// </summary>
public enum LicenceCategory
{
    B = 1,
    C = 2,
    CE = 3,
}
