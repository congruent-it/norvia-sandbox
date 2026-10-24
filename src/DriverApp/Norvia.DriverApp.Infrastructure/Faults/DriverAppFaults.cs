using Norvia.DriverApp.Application.Faults;
using Norvia.ServiceDefaults.Faults;

namespace Norvia.DriverApp.Infrastructure.Faults;

internal sealed class DriverAppFaults(IFaults faults) : IDriverAppFaults
{
    public bool IsUnavailable => faults.IsOn(DriverAppFaultNames.Unavailable);

    public bool HeicPhoto => faults.IsOn(DriverAppFaultNames.HeicPhoto);
}
