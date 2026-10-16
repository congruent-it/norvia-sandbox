using Ardalis.Result;
using Mediator;

namespace Norvia.PartnerPortal.Application.Reset;

/// <summary>Returns the Partner portal to a clean sheet. Refused while the setting is off.</summary>
public sealed record ResetEstateCommand : ICommand<Result<ResetReport>>;
