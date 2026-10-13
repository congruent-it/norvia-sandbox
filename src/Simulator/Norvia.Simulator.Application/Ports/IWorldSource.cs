using Norvia.Simulator.Domain.Worlds;

namespace Norvia.Simulator.Application.Ports;

/// <summary>The Shippers, Personas and Rate Cards a plan is made from: the seed file, until the ERP is built (ADR 0002).</summary>
public interface IWorldSource
{
    SimulationWorld Load();
}
