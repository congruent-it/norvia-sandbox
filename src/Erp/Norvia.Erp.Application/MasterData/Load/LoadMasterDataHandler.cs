using Ardalis.SharedKernel;
using Mediator;
using Norvia.Erp.Domain.Carriers;
using Norvia.Erp.Domain.Drivers;
using Norvia.Erp.Domain.RateCards;
using Norvia.Erp.Domain.Shippers;
using Norvia.Erp.Domain.Vehicles;

namespace Norvia.Erp.Application.MasterData.Load;

public sealed class LoadMasterDataHandler(
    IRepository<Shipper> shippers,
    IRepository<Carrier> carriers,
    IRepository<RateCard> rateCards,
    IRepository<CarrierRateCard> carrierRateCards,
    IRepository<Vehicle> vehicles,
    IRepository<Driver> drivers) : ICommandHandler<LoadMasterDataCommand, LoadMasterDataResult>
{
    public async ValueTask<LoadMasterDataResult> Handle(LoadMasterDataCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var seed = command.Seed;
        var added = 0;
        var reloaded = 0;

        foreach (var row in seed.Shippers)
        {
            var id = new ShipperId(row.Id);
            if (await shippers.FirstOrDefaultAsync(new ShipperByIdSpec(id), cancellationToken) is { } existing)
            {
                existing.Reload(row.Name, row.Active);
                await shippers.UpdateAsync(existing, cancellationToken);
                reloaded++;
            }
            else
            {
                await shippers.AddAsync(new Shipper(id, row.Name, row.Active), cancellationToken);
                added++;
            }
        }

        foreach (var row in seed.Carriers)
        {
            var id = new CarrierId(row.Id);
            if (await carriers.FirstOrDefaultAsync(new CarrierByIdSpec(id), cancellationToken) is { } existing)
            {
                existing.Reload(row.Name, row.Active);
                await carriers.UpdateAsync(existing, cancellationToken);
                reloaded++;
            }
            else
            {
                await carriers.AddAsync(new Carrier(id, row.Name, row.Active), cancellationToken);
                added++;
            }
        }

        foreach (var row in seed.RateCards)
        {
            var id = new RateCardId(row.Id);
            if (await rateCards.FirstOrDefaultAsync(new RateCardByIdSpec(id), cancellationToken) is { } existing)
            {
                existing.Reload(new ShipperId(row.PartyId), row.Versions);
                await rateCards.UpdateAsync(existing, cancellationToken);
                reloaded++;
            }
            else
            {
                await rateCards.AddAsync(new RateCard(id, new ShipperId(row.PartyId), row.Versions), cancellationToken);
                added++;
            }
        }

        foreach (var row in seed.CarrierRateCards)
        {
            var id = new RateCardId(row.Id);
            if (await carrierRateCards.FirstOrDefaultAsync(new CarrierRateCardByIdSpec(id), cancellationToken) is { } existing)
            {
                existing.Reload(new CarrierId(row.PartyId), row.Versions);
                await carrierRateCards.UpdateAsync(existing, cancellationToken);
                reloaded++;
            }
            else
            {
                await carrierRateCards.AddAsync(new CarrierRateCard(id, new CarrierId(row.PartyId), row.Versions), cancellationToken);
                added++;
            }
        }

        foreach (var row in seed.Vehicles)
        {
            var id = new VehicleId(row.Id);
            if (await vehicles.FirstOrDefaultAsync(new VehicleByIdSpec(id), cancellationToken) is { } existing)
            {
                existing.Reload(row.Registration, row.Type, row.PayloadKg, row.PalletPlaces, row.Active);
                await vehicles.UpdateAsync(existing, cancellationToken);
                reloaded++;
            }
            else
            {
                await vehicles.AddAsync(new Vehicle(id, row.Registration, row.Type, row.PayloadKg, row.PalletPlaces, row.Active), cancellationToken);
                added++;
            }
        }

        foreach (var row in seed.Drivers)
        {
            var id = new DriverId(row.Id);
            if (await drivers.FirstOrDefaultAsync(new DriverByIdSpec(id), cancellationToken) is { } existing)
            {
                existing.Reload(row.Name, row.Licences, row.Active);
                await drivers.UpdateAsync(existing, cancellationToken);
                reloaded++;
            }
            else
            {
                await drivers.AddAsync(new Driver(id, row.Name, row.Licences, row.Active), cancellationToken);
                added++;
            }
        }

        return new LoadMasterDataResult(added, reloaded);
    }
}
