namespace Norvia.Erp.Application.MasterData.Drivers;

/// <param name="Licences">The licence categories held by name (B, C, CE), each once, lowest first.</param>
/// <param name="Revision">The Master-data revision: 1 as loaded from the seed file, raised by every edit.</param>
public sealed record DriverView(string Id, string Name, IReadOnlyList<string> Licences, bool Active, int Revision = 1);
