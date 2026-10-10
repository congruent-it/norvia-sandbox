using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Settings;

namespace Norvia.DriverApp.Application.Settings.GetCurrent;

public sealed class GetSettingsHandler(IReadRepository<DriverAppSettings> settings) : IQueryHandler<GetSettingsQuery, DriverAppSettingsView>
{
    public async ValueTask<DriverAppSettingsView> Handle(GetSettingsQuery query, CancellationToken cancellationToken) =>
        new((await settings.GetByIdAsync(DriverAppSettings.SingleId, cancellationToken))?.Unavailable ?? false);
}
