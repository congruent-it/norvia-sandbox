namespace Norvia.Simulator.Web.IntegrationTests;

// One run is active at a time, so the tests that start runs take turns.
[CollectionDefinition("Simulator runs")]
public sealed class SimulatorRunsDefinition;
