using Ardalis.Result;
using Mediator;

namespace Norvia.Tms.Application.Reset;

/// <summary>Returns the TMS to a clean sheet. Refused while the setting is off or a relevant Service Bus subscription holds unprocessed messages.</summary>
public sealed record ResetEstateCommand : ICommand<Result<ResetReport>>;
