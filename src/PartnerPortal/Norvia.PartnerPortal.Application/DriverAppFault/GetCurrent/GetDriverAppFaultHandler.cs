using Mediator;

namespace Norvia.PartnerPortal.Application.DriverAppFault.GetCurrent;

public sealed class GetDriverAppFaultHandler(IDriverAppSettings driverApp) : IQueryHandler<GetDriverAppFaultQuery, DriverAppFaultView>
{
    public async ValueTask<DriverAppFaultView> Handle(GetDriverAppFaultQuery query, CancellationToken cancellationToken) =>
        new(await driverApp.IsUnavailableAsync(cancellationToken));
}
