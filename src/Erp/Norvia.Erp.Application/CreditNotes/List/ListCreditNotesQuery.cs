using Mediator;

namespace Norvia.Erp.Application.CreditNotes.List;

/// <summary>The Credit notes, the newest first; only those of one Shipper invoice when its number is given.</summary>
public sealed record ListCreditNotesQuery(string? InvoiceNumber = null) : IQuery<IReadOnlyList<CreditNoteView>>;
