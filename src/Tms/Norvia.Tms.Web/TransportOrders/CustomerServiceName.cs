namespace Norvia.Tms.Web.TransportOrders;

/// <summary>
/// Who the TMS UI names as Customer service on what it sends: who released a Transport Order without a Proof of delivery.
/// The UI has no login, only the Role switcher (GLOSSARY.md), so every Customer service employee is the demo's.
/// </summary>
public static class CustomerServiceName
{
    public const string Demo = "Sanne Bakker";
}
