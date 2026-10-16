using Ardalis.Result;
using Mediator;

namespace Norvia.Simulator.Application.Reset;

/// <summary>Returns the estate to a clean sheet through each application's own reset door, then clears the Simulator's runs, logs and run counter.</summary>
public sealed record ResetEstateCommand : ICommand<Result<EstateResetReport>>;
