using Microsoft.Extensions.Logging;
using Norvia.Simulator.Application.Ports;
using Norvia.Simulator.Domain.Personas;
using Norvia.Simulator.Domain.Plans;
using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Plans.Pools;

/// <summary>
/// Gives a Simulation plan its pools of Carrier texts: one for each profile its Carriers write in and each kind of text. A pool is the
/// built-in one unless the Agents are on, the plan's cap on generations allows it and the personas agent wrote enough valid texts. Whatever
/// goes wrong with the model is a note on the plan and the built-in pool, never a failed plan: no Agent is called while a run is sending.
/// </summary>
public sealed partial class PlanPools(
    IPersonaProfiles profiles, IBuiltInPools builtIn, IAgentClient agents, PersonaSettings settings, ILogger<PlanPools> logger)
{
    public async Task<SimulationPlan> AddToAsync(SimulationPlan plan, SimulationWorld world, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(world);

        var carrierProfiles = world.ActiveCarriers
            .Where(carrier => carrier.HasPersona)
            .Select(carrier => (carrier.Id, Profile: profiles.ProfileOf(carrier.Id)))
            .Where(pair => pair.Profile is not null)
            .ToDictionary(pair => pair.Id, pair => pair.Profile!, StringComparer.Ordinal);
        if (carrierProfiles.Count == 0)
        {
            return plan;
        }

        var pools = new List<TextPool>();
        var notes = new List<string>();
        var generations = 0;
        foreach (var profile in carrierProfiles.Values.DistinctBy(profile => profile.Id).OrderBy(profile => profile.Id, StringComparer.Ordinal))
        {
            foreach (var category in Enum.GetValues<TextCategory>())
            {
                TextPool? generated = null;
                if (settings.AgentsEnabled && generations < settings.MaxGenerationsPerPlan)
                {
                    generations++;
                    generated = await GenerateAsync(profile, category, plan.RunNumber, notes, cancellationToken);
                }
                else if (settings.AgentsEnabled)
                {
                    notes.Add($"The plan's cap of {settings.MaxGenerationsPerPlan} generations was reached: the {profile.Id} {category} pool is the built-in one.");
                }

                if (generated is not null)
                {
                    pools.Add(generated);
                    continue;
                }

                var texts = builtIn.TextsOf(profile, category);
                if (texts.Count == 0)
                {
                    notes.Add($"There is no built-in {profile.Id} {category} pool: its Carriers send the fixed words.");
                    continue;
                }

                pools.Add(new TextPool(profile.Id, profile.Language, category, TextPool.BuiltInSource, texts));
            }
        }

        var kept = pools.Select(pool => pool.ProfileId).ToHashSet(StringComparer.Ordinal);
        return plan with
        {
            Pools = pools,
            CarrierProfiles = carrierProfiles.Where(pair => kept.Contains(pair.Value.Id)).ToDictionary(pair => pair.Key, pair => pair.Value.Id, StringComparer.Ordinal),
            PoolNotes = notes,
        };
    }

    // Null when the model failed or left too few valid texts; the note says which, and the caller falls back to the built-in pool.
    private async Task<TextPool?> GenerateAsync(PersonaProfile profile, TextCategory category, int runNumber, List<string> notes, CancellationToken cancellationToken)
    {
        try
        {
            var result = await agents.GeneratePoolAsync(new PoolRequest(profile, category, settings.PoolSize), cancellationToken);
            if (!result.IsSuccess)
            {
                notes.Add($"The model couldn't write the {profile.Id} {category} texts: the built-in pool is used.");
                LogFailed(logger, profile.Id, category);
                return null;
            }

            var valid = PoolTextRules.Acceptable(result.Value);
            if (valid.Count < PoolTextRules.MinPoolSize)
            {
                notes.Add($"The model wrote only {valid.Count} usable {profile.Id} {category} texts: the built-in pool is used.");
                LogTooSmall(logger, profile.Id, category, valid.Count);
                return null;
            }

            return new TextPool(profile.Id, profile.Language, category, TextPool.AgentSource(runNumber), valid);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            notes.Add($"The model couldn't write the {profile.Id} {category} texts: the built-in pool is used.");
            LogFailed(logger, profile.Id, category, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The personas agent gave no pool for {ProfileId} {Category}: the built-in pool is used")]
    private static partial void LogFailed(ILogger logger, string profileId, TextCategory category, Exception? exception = null);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The personas agent gave {Usable} usable texts for {ProfileId} {Category}: the built-in pool is used")]
    private static partial void LogTooSmall(ILogger logger, string profileId, TextCategory category, int usable);
}
