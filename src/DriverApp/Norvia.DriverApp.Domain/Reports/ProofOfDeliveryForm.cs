namespace Norvia.DriverApp.Domain.Reports;

/// <summary>What the Driver filled in on the Proof of delivery form, with the files as <see cref="ProofFileInfo"/>.</summary>
/// <param name="Remark">What the Driver says of the reservations; required with reservations, and not kept otherwise.</param>
/// <param name="Signature">The signature drawn on screen, as a PNG.</param>
/// <param name="Photos">0 to 4 photos, JPEG or PNG.</param>
public sealed record ProofOfDeliveryForm(string? ReceivedBy, DeliveryCondition? Condition, string? Remark, ProofFileInfo? Signature, IReadOnlyList<ProofFileInfo> Photos);
