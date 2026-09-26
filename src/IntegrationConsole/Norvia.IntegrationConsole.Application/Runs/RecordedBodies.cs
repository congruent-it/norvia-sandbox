namespace Norvia.IntegrationConsole.Application.Runs;

/// <summary>A step's input and output bodies as run history has them.</summary>
public sealed record RecordedBodies(RecordedBody Input, RecordedBody Output);
