using TMConcept.Core.Keys;

namespace TMConcept.Core.Models;

/// <summary>The model chosen for a step together with the key to call it with.</summary>
public sealed record ResolvedModel(CatalogModel Model, ApiKey Key, string Effort);
