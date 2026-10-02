namespace Norvia.PartnerPortal.Application.CarrierBookings.AutoAnswer;

/// <summary>Tells whoever answers automatically that a request with an automatic answer has arrived, so it looks at the clock again.</summary>
public interface IAutoAnswerSignal
{
    void RequestArrived();
}
