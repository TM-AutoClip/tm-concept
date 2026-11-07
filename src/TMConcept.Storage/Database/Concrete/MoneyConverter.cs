using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TMConcept.Storage.Database;

/// <summary>Stores money as invariant TEXT (<see cref="ConceptSchema.MoneyFormat"/>: "1.0", "12.5", "0.0123"), as every version did; exact, unlike REAL.</summary>
internal sealed class MoneyConverter() : ValueConverter<decimal, string>(
    v => v.ToString(ConceptSchema.MoneyFormat, CultureInfo.InvariantCulture),
    v => decimal.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture));
