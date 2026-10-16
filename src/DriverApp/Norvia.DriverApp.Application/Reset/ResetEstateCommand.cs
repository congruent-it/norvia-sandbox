using Ardalis.Result;
using Mediator;

namespace Norvia.DriverApp.Application.Reset;

/// <summary>Returns the Driver app to a clean sheet. Refused while the setting is off or a relevant Service Bus subscription holds unprocessed messages.</summary>
public sealed record ResetEstateCommand : ICommand<Result<ResetReport>>;
