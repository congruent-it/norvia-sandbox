namespace Norvia.IntegrationConsole.Application.Trails;

/// <summary>A business key a Message trail carried, such as its Transport Order number.</summary>
public sealed record BusinessKey(BusinessKeyKind Kind, string Value);
