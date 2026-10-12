using Mediator;

namespace Norvia.PartnerPortal.Application.DriverAppFault.Switch;

public sealed class SetDriverAppFaultHandler(IDriverAppSettings driverApp) : ICommandHandler<SetDriverAppFaultCommand, DriverAppFaultView>
{
    public async ValueTask<DriverAppFaultView> Handle(SetDriverAppFaultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await driverApp.SetUnavailableAsync(command.Unavailable, cancellationToken);
        return new DriverAppFaultView(command.Unavailable);
    }
}
