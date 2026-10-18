using Mediator;

namespace Norvia.Erp.Application.MasterData.Load;

/// <summary>Loads the seed file's master data. Safe to run again: what is there is reloaded, not duplicated.</summary>
public sealed record LoadMasterDataCommand(MasterDataSeed Seed) : ICommand<LoadMasterDataResult>;

/// <param name="Added">Records that were not there yet.</param>
/// <param name="Reloaded">Records that were there already and now say what the seed file says.</param>
public sealed record LoadMasterDataResult(int Added, int Reloaded);
