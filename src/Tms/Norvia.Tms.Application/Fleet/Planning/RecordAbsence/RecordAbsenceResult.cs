namespace Norvia.Tms.Application.Fleet.Planning.RecordAbsence;

/// <param name="Days">How many days the Driver is now absent because of this action.</param>
public sealed record RecordAbsenceResult(DateOnly From, DateOnly To, int Days);
