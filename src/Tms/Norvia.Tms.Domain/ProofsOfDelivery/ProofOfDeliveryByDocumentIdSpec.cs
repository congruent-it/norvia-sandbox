using Ardalis.Specification;

namespace Norvia.Tms.Domain.ProofsOfDelivery;

/// <summary>The Proof of delivery a source already recorded under its own document ID, which tells a repeat from a new one.</summary>
public sealed class ProofOfDeliveryByDocumentIdSpec : SingleResultSpecification<ProofOfDelivery>
{
    public ProofOfDeliveryByDocumentIdSpec(ProofOfDeliverySource source, string documentId)
    {
        Source = source;
        DocumentId = documentId;
        Query.Where(proofOfDelivery => proofOfDelivery.Source == source && proofOfDelivery.DocumentId == documentId).AsNoTracking();
    }

    public ProofOfDeliverySource Source { get; }

    public string DocumentId { get; }
}
