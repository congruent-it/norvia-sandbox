namespace Norvia.PartnerPortal.Application.Reset;

/// <summary>The Partner portal's database as a reset sees it.</summary>
public interface ITransactionalData
{
    /// <summary>Deletes what each Persona sent Norvia and received, and the Personas' answer and tracking modes, which fall back to the seed file's.</summary>
    Task<IReadOnlyList<DeletedKind>> ClearAsync(CancellationToken cancellationToken);
}
