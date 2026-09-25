namespace Norvia.PartnerPortal.Web.Bookings;

/// <summary>Something the form can't send, and the field it belongs to, so the message shows next to it.</summary>
/// <param name="Model">The object that holds the field: the form, its Consignee or one of its goods lines.</param>
public sealed record FormProblem(object Model, string Field, string Message);
