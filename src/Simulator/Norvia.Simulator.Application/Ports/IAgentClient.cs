using Ardalis.Result;
using Norvia.Simulator.Domain.Personas;

namespace Norvia.Simulator.Application.Ports;

/// <summary>
/// The Simulator's port to an Agent (ADR 0040): the personas agent, which writes a pool of realistic texts for one profile, category and
/// language. Infrastructure implements it, locally by the in-process runner with a scripted fake model. A failure comes back as
/// <see cref="ResultStatus.Unavailable"/>; an Agent is never on a critical path, and no Agent is called while a run is sending.
/// </summary>
public interface IAgentClient
{
    Task<Result<IReadOnlyList<string>>> GeneratePoolAsync(PoolRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// What the model is told, and nothing more: the profile, the language and the category. No booking, no Consignee and no number of the plan
/// is in it.
/// </summary>
public sealed record PoolRequest(PersonaProfile Profile, TextCategory Category, int Count);
