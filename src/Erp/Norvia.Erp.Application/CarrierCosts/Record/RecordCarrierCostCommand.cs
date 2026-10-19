using Ardalis.Result;
using Mediator;
using Norvia.Erp.Domain.CarrierCosts;

namespace Norvia.Erp.Application.CarrierCosts.Record;

/// <summary>Records the Carrier cost <c>CarrierCostIncurred</c> carries, as Open. Publishes nothing.</summary>
public sealed record RecordCarrierCostCommand(CarrierCostData Cost) : ICommand<Result<RecordCarrierCostResult>>;

/// <param name="Cost">The recorded Carrier cost, or the original one for a repeat.</param>
/// <param name="Repeat">True when the Carrier booking already had this Carrier cost.</param>
public sealed record RecordCarrierCostResult(CarrierCostView Cost, bool Repeat);
