using Microsoft.EntityFrameworkCore;
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
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<CarrierRateCardCopy>().AsNoTracking();
        if (copies.Where(row => row.CarrierId == carrierId.Value).OrderByDescending(row => row.Revision).ThenBy(row => row.Id).FirstOrDefault() is { } copy)
        {
            return Of(copy);
        }

        // A seed card the ERP has since moved to another Carrier no longer belongs to this one.
        var seeded = seed.FindFor(carrierId);
        return seeded is not null && !copies.Any(row => row.Id == seeded.Id.Value) ? seeded : null;
    }

    public CarrierRateCard? Find(RateCardId id)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copy = context.Set<CarrierRateCardCopy>().AsNoTracking().SingleOrDefault(row => row.Id == id.Value);
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public async Task ReplaceAsync(CarrierRateCard carrierRateCard, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(carrierRateCard);
        ArgumentNullException.ThrowIfNull(versions);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<CarrierRateCardCopy>();
        var copy = await copies.SingleOrDefaultAsync(row => row.Id == carrierRateCard.Id.Value, cancellationToken);
        if (copy is not null && copy.Revision >= carrierRateCard.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return;
        }

        if (copy is null)
        {
            copy = new CarrierRateCardCopy { Id = carrierRateCard.Id.Value };
            copies.Add(copy);
        }

        copy.CarrierId = carrierRateCard.CarrierId.Value;
        copy.Revision = carrierRateCard.Revision;
        copy.VersionsJson = RateCardVersionsJson.Serialize(versions);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static CarrierRateCard Of(CarrierRateCardCopy copy) =>
        new(new RateCardId(copy.Id), new CarrierId(copy.CarrierId), RateCardVersionsJson.Read(copy.VersionsJson), copy.Revision);
}
