namespace Norvia.Erp.Application.MasterData.Shippers.List;

/// <param name="Revision">The Master-data revision: 1 as loaded from the seed file, raised by every edit.</param>
public sealed record ShipperView(string Id, string Name, bool Active, int Revision = 1);
