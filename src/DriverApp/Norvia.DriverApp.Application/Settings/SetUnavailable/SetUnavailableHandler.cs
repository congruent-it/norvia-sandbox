using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Settings;

namespace Norvia.DriverApp.Application.Settings.SetUnavailable;

public sealed class SetUnavailableHandler(IRepository<DriverAppSettings> settings) : ICommandHandler<SetUnavailableCommand>
{
    public async ValueTask<Unit> Handle(SetUnavailableCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stored = await settings.GetByIdAsync(DriverAppSettings.SingleId, cancellationToken);
        var isNew = stored is null;
        stored ??= new DriverAppSettings();
        stored.SetUnavailable(command.Unavailable);

        if (isNew)
        {
            await settings.AddAsync(stored, cancellationToken);
        }
        else
        {
            await settings.UpdateAsync(stored, cancellationToken);
        }

        return Unit.Value;
    }
}
