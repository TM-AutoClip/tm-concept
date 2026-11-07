using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TMConcept.Storage.Database;

/// <summary>Stores a <see cref="Guid"/> as upper-case TEXT ("D" form), as every version did (EF Core's SQLite default, written out so it cannot drift).</summary>
internal sealed class StoredIdConverter() : ValueConverter<Guid, string>(
    v => v.ToString(ConceptSchema.IdFormat).ToUpperInvariant(),
    v => Guid.Parse(v));
