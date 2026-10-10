using Ardalis.SharedKernel;
using Mediator;
using Norvia.DriverApp.Domain.Settings;

namespace Norvia.DriverApp.Application.Settings.SetUnavailable;

public sealed class SetUnavailableHandler(IRepository<DriverAppSettings> settings) : ICommandHandler<SetUnavailableCommand>
{
    public async ValueTask<Unit> Handle(SetUnavailableCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await settings.GetByIdAsync(DriverAppSettings.SingleId, cancellationToken) is { } held)
        {
            held.SetUnavailable(command.Unavailable);
            await settings.UpdateAsync(held, cancellationToken);
            return Unit.Value;
        }

        var created = new DriverAppSettings();
        created.SetUnavailable(command.Unavailable);
        await settings.AddAsync(created, cancellationToken);
        return Unit.Value;
    }
}
