namespace Norvia.DriverApp.Application.Reports;

/// <summary>What the Driver does at a Stop. The first three complete it; Damaged, reported on any Stop, doesn't.</summary>
public enum StopActionDto
{
    Collected,
    CollectionFailed,
    DeliveryFailed,
    Damaged,
}
