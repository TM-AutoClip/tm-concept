using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TMConcept.Storage.Database;

/// <summary>
/// Stores <see cref="DateTimeOffset"/> as UTC ticks: SQLite has no date type and EF Core cannot order or compare DateTimeOffset there;
/// a number sorts and compares correctly.
/// </summary>
public sealed class UtcTicksConverter() : ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
