using Ardalis.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>
/// The TMS's read copy of the Carriers' Carrier Rate Cards: the seed file's first load at Master-data revision 1, with every Carrier Rate Card the ERP
/// has replicated since laid over it (E3). It reads the replicas on every call, so every instance of the TMS sees a replacement at once.
/// </summary>
internal sealed class ReplicatedCarrierRateCardDirectory(SeedFileCarrierRateCardDirectory seed, IServiceScopeFactory scopes) : ICarrierRateCardDirectory, ICarrierRateCardReplicas
{
    public CarrierRateCard? FindFor(CarrierId carrierId)
    {
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<CarrierRateCardCopy>>();
        if (copies.FirstOrDefault(new CarrierRateCardCopyOfCarrierSpec(carrierId.Value)) is { } copy)
        {
            return Of(copy);
        }

        // A seed card the ERP has since moved to another Carrier no longer belongs to this one.
        var seeded = seed.FindFor(carrierId);
        return seeded is not null && copies.FirstOrDefault(new CarrierRateCardCopyByIdSpec(seeded.Id.Value)) is null ? seeded : null;
    }

    public CarrierRateCard? Find(RateCardId id)
    {
        using var scope = scopes.CreateScope();
        var copy = scope.ServiceProvider.GetRequiredService<ISyncReadRepository<CarrierRateCardCopy>>().FirstOrDefault(new CarrierRateCardCopyByIdSpec(id.Value));
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public async Task ReplaceAsync(CarrierRateCard carrierRateCard, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carrierRateCard);
        ArgumentNullException.ThrowIfNull(versions);
        using var scope = scopes.CreateScope();
        var copies = scope.ServiceProvider.GetRequiredService<IRepository<CarrierRateCardCopy>>();
        var copy = await copies.FirstOrDefaultAsync(new CarrierRateCardCopyForReplaceSpec(carrierRateCard.Id.Value), cancellationToken);
        if (copy is not null && copy.Revision >= carrierRateCard.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return;
        }

        var isNew = copy is null;
        copy ??= new CarrierRateCardCopy { Id = carrierRateCard.Id.Value };

        copy.CarrierId = carrierRateCard.CarrierId.Value;
        copy.Revision = carrierRateCard.Revision;
        copy.VersionsJson = RateCardVersionsJson.Serialize(versions);
        await SaveAsync(copies, copy, isNew, cancellationToken);
    }

    private static async Task SaveAsync(IRepository<CarrierRateCardCopy> copies, CarrierRateCardCopy copy, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            await copies.AddAsync(copy, cancellationToken);
        }
        else
        {
            await copies.UpdateAsync(copy, cancellationToken);
        }
    }

    private static CarrierRateCard Of(CarrierRateCardCopy copy) =>
        new(new RateCardId(copy.Id), new CarrierId(copy.CarrierId), RateCardVersionsJson.Read(copy.VersionsJson), copy.Revision);
}
