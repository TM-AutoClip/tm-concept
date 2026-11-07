using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TMConcept.Storage.Database;

/// <summary>Stores a flag as INTEGER 0/1, as every version did.</summary>
internal sealed class FlagConverter() : ValueConverter<bool, long>(v => v ? 1L : 0L, v => v != 0);
