using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TMConcept.Storage.Database;

/// <summary>
/// Stores <see cref="DateTimeOffset"/> as UTC ticks (INTEGER), as every version did: SQLite has no date type and a number sorts and compares
/// correctly (EF Core cannot order or compare DateTimeOffset there).
/// </summary>
internal sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
