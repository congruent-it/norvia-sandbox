using Mediator;

namespace Norvia.PartnerPortal.Application.Faults.Switch;

public sealed class SwitchFaultHandler(IFaultSwitches faults) : ICommandHandler<SwitchFaultCommand, FaultView>
{
    public async ValueTask<FaultView> Handle(SwitchFaultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await faults.SwitchAsync(command.Owner, command.Name, command.Duration, cancellationToken);
    }
}
