using CasCap.Entities;

namespace CasCap.Models;

/// <summary>A committed new intent permits exactly one submission; an existing intent permits only receipt recovery.</summary>
internal sealed record DocumentPreparationReservation(DocumentPreparationOperationEntity Operation, bool IsNew);
