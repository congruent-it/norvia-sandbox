namespace Norvia.Tms.Web.Exceptions;

/// <summary>One line of an Exception's story on a Shipment's timeline: what happened, when and the detail of it.</summary>
public sealed record ExceptionHappening(DateTimeOffset When, string What, string Detail);
