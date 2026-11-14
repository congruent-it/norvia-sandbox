using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.Carriers;
using Norvia.Tms.Domain.RateCards;

namespace Norvia.Tms.Application.RateCards.ReplaceCarrier;

public sealed partial class ReplaceCarrierRateCardHandler(ICarrierRateCardDirectory directory, ICarrierRateCardReplicas replicas, ILogger<ReplaceCarrierRateCardHandler> logger)
    : ICommandHandler<ReplaceCarrierRateCardCommand, Result<ReplaceCarrierRateCardResult>>
{
    public async ValueTask<Result<ReplaceCarrierRateCardResult>> Handle(ReplaceCarrierRateCardCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.CarrierId) || command.Revision < 1)
        {
            return Refuse("A Carrier Rate Card needs an ID, a Carrier and a Master-data revision of at least 1.", "MasterDataUnreadable");
        }

        IReadOnlyList<RateCardVersion> versions;
        try
        {
            versions = RateCardVersionMapper.ToDomain(command.Versions ?? []);
        }
        catch (ArgumentException exception)
        {
            return Refuse($"A version of the Carrier Rate Card can't be read: {exception.Message}", "MasterDataUnreadable");
        }

        if (RateCardVersionRules.Problem(versions) is { } problem)
        {
            return Refuse(problem, problem.Contains("overlap", StringComparison.Ordinal) ? "RateCardVersionsOverlap" : "MasterDataUnreadable");
        }

        var id = new RateCardId(command.Id);
        using var trail = logger.Track(new() { [TrackingKeys.PartyId] = command.CarrierId });
        var current = directory.Find(id)?.Revision ?? 0;
        if (command.Revision <= current)
        {
            using var stale = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStale(logger, command.Id, command.Revision, current);
            return new ReplaceCarrierRateCardResult(Applied: false, current);
        }

        await replicas.ReplaceAsync(new CarrierRateCard(id, new CarrierId(command.CarrierId), versions, command.Revision), command.Versions!, cancellationToken);
        using var replaced = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        LogReplaced(logger, command.Id, command.Revision, versions.Count);
        return new ReplaceCarrierRateCardResult(Applied: true, command.Revision);
    }

    private Result<ReplaceCarrierRateCardResult> Refuse(string message, string code)
    {
        using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = code });
        return Result<ReplaceCarrierRateCardResult>.Invalid(new ValidationError("CarrierRateCard", message, code, ValidationSeverity.Error));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier Rate Card {RateCardId} replaced at Master-data revision {Revision} with {VersionCount} versions")]
    private static partial void LogReplaced(ILogger logger, string rateCardId, int revision, int versionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Carrier Rate Card {RateCardId} revision {Revision} is not higher than the copy's {Current}; nothing changed")]
    private static partial void LogStale(ILogger logger, string rateCardId, int revision, int current);
}
