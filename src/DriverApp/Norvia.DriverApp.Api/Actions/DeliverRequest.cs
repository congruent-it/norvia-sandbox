using Norvia.DriverApp.Application.Reports.Deliver;

namespace Norvia.DriverApp.Api.Actions;

/// <summary>The Proof of delivery form as the Driver fills it in: who received the goods, in what condition, and the files, which travel as base64.</summary>
public sealed record DeliverRequest(string? ReceivedBy, DeliveryConditionDto? Condition, string? Remark, ProofFileRequest? Signature, IReadOnlyList<ProofFileRequest>? Photos);
