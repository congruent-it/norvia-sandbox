using Ardalis.Result;
using Mediator;
using Microsoft.Extensions.Logging;
using Norvia.Tms.Application.IntegrationEvents;
using Norvia.Tms.Domain.RateCards;
using Norvia.Tms.Domain.Shippers;

namespace Norvia.Tms.Application.RateCards.Replace;

public sealed partial class ReplaceRateCardHandler(IRateCardDirectory directory, IRateCardReplicas replicas, ILogger<ReplaceRateCardHandler> logger)
    : ICommandHandler<ReplaceRateCardCommand, Result<ReplaceRateCardResult>>
{
    public async ValueTask<Result<ReplaceRateCardResult>> Handle(ReplaceRateCardCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id) || string.IsNullOrWhiteSpace(command.ShipperId) || command.Revision < 1)
        {
            return Refuse("A Rate Card needs an ID, a Shipper and a Master-data revision of at least 1.", "MasterDataUnreadable");
        }

        IReadOnlyList<RateCardVersion> versions;
        try
        {
            versions = RateCardVersionMapper.ToDomain(command.Versions ?? []);
        }
        catch (ArgumentException exception)
        {
            return Refuse($"A version of the Rate Card can't be read: {exception.Message}", "MasterDataUnreadable");
        }

        if (RateCardVersionRules.Problem(versions) is { } problem)
        {
            return Refuse(problem, problem.Contains("overlap", StringComparison.Ordinal) ? "RateCardVersionsOverlap" : "MasterDataUnreadable");
        }

        var id = new RateCardId(command.Id);
        using var trail = logger.Track(new() { [TrackingKeys.PartyId] = command.ShipperId });
        var current = directory.Find(id)?.Revision ?? 0;
        if (command.Revision <= current)
        {
            using var stale = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
            LogStale(logger, command.Id, command.Revision, current);
            return new ReplaceRateCardResult(Applied: false, current);
        }

        await replicas.ReplaceAsync(new RateCard(id, new ShipperId(command.ShipperId), versions, command.Revision), command.Versions!, cancellationToken);
        using var replaced = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Succeeded });
        LogReplaced(logger, command.Id, command.Revision, versions.Count);
        return new ReplaceRateCardResult(Applied: true, command.Revision);
    }

    private Result<ReplaceRateCardResult> Refuse(string message, string code)
    {
        using var refused = logger.Track(new() { [TrackingKeys.Outcome] = TrackingKeys.Refused, [TrackingKeys.FailureCode] = code });
        return Result<ReplaceRateCardResult>.Invalid(new ValidationError("RateCard", message, code, ValidationSeverity.Error));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate Card {RateCardId} replaced at Master-data revision {Revision} with {VersionCount} versions")]
    private static partial void LogReplaced(ILogger logger, string rateCardId, int revision, int versionCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rate Card {RateCardId} revision {Revision} is not higher than the copy's {Current}; nothing changed")]
    private static partial void LogStale(ILogger logger, string rateCardId, int revision, int current);
}
