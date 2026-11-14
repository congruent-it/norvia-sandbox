using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Norvia.Tms.Application.RateCards;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;
using Norvia.Tms.Infrastructure.Data;

namespace Norvia.Tms.Infrastructure.RateCards;

/// <summary>
/// The TMS's read copy of the Shippers' Rate Cards: the seed file's first load at Master-data revision 1, with every Rate Card the ERP
/// has replicated since laid over it (E3). It reads the replicas on every call, so every instance of the TMS sees a replacement at once.
/// </summary>
internal sealed class ReplicatedRateCardDirectory(SeedFileRateCardDirectory seed, IServiceScopeFactory scopes) : IRateCardDirectory, IRateCardReplicas
{
    public RateCard? FindFor(ShipperId shipperId)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<RateCardCopy>().AsNoTracking();
        if (copies.Where(row => row.ShipperId == shipperId.Value).OrderByDescending(row => row.Revision).ThenBy(row => row.Id).FirstOrDefault() is { } copy)
        {
            return Of(copy);
        }

        // A seed card the ERP has since moved to another Shipper no longer belongs to this one.
        var seeded = seed.FindFor(shipperId);
        return seeded is not null && !copies.Any(row => row.Id == seeded.Id.Value) ? seeded : null;
    }

    public RateCard? Find(RateCardId id)
    {
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copy = context.Set<RateCardCopy>().AsNoTracking().SingleOrDefault(row => row.Id == id.Value);
        return copy is null ? seed.Find(id) : Of(copy);
    }

    public async Task ReplaceAsync(RateCard rateCard, IReadOnlyList<RateCardVersionData> versions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rateCard);
        ArgumentNullException.ThrowIfNull(versions);
        using var scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TmsDbContext>();
        var copies = context.Set<RateCardCopy>();
        var copy = await copies.SingleOrDefaultAsync(row => row.Id == rateCard.Id.Value, cancellationToken);
        if (copy is not null && copy.Revision >= rateCard.Revision)
        {
            // A delivery that lost a race with a later revision must not move the copy backwards.
            return;
        }

        if (copy is null)
        {
            copy = new RateCardCopy { Id = rateCard.Id.Value };
            copies.Add(copy);
        }

        copy.ShipperId = rateCard.ShipperId.Value;
        copy.Revision = rateCard.Revision;
        copy.VersionsJson = RateCardVersionsJson.Serialize(versions);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static RateCard Of(RateCardCopy copy) =>
        new(new RateCardId(copy.Id), new ShipperId(copy.ShipperId), RateCardVersionsJson.Read(copy.VersionsJson), copy.Revision);
}
