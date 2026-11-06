namespace Norvia.Simulator.Domain.Personas;

/// <summary>Which of the kinds of text a Carrier sends and which a Driver sends.</summary>
public static class TextCategories
{
    public static IReadOnlyList<TextCategory> ForCarriers { get; } = [TextCategory.DeclineReason, TextCategory.DeliveryFailedRemark];

    public static IReadOnlyList<TextCategory> ForDrivers { get; } =
        [TextCategory.DriverCollectionFailedRemark, TextCategory.DriverDeliveryFailedRemark, TextCategory.DriverDamageRemark];

    public static bool IsDriverText(TextCategory category) => ForDrivers.Contains(category);
}
