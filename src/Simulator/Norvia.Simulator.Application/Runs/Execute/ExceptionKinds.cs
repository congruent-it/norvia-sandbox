namespace Norvia.Simulator.Application.Runs.Execute;

/// <summary>The kinds of Exception a Driver's report opens at the TMS (P7), as its Open Exceptions list names them once spaces are left out.</summary>
internal static class ExceptionKinds
{
    public const string CollectionFailed = "CollectionFailed";

    public const string DeliveryFailed = "DeliveryFailed";

    public const string Damaged = "Damaged";

    public const string Reservations = "Reservations";
}
