namespace Norvia.Tms.Web.Exceptions;

/// <summary>What a Planner can do about an open Exception (P7), as the Open Exceptions list offers it.</summary>
public enum ExceptionActionKind
{
    NotifyShipper,
    CancelCarrierBooking,
    AgreeDeliveryWindow,
    AddNote,
    Resolve,
}
