#if DEBUG
using CasCap.Client.Models;

namespace CasCap.Models;

/// <summary>Keeps private indexing inputs server-side while exposing only bounded eligibility metadata.</summary>
internal sealed record DocumentIndexingPreflight(DocumentIndexingPreflightResponse Response, DocumentIndexingManifest? Manifest);
#endif
