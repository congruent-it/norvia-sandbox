namespace Norvia.Simulator.Domain.Plans;

/// <summary>A collection window as local date and times, in the time zone of the collection address.</summary>
public sealed record CollectionWindow(DateTime From, DateTime Until);
