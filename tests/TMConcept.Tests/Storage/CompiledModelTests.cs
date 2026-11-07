using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using TMConcept.Storage.Database;

namespace TMConcept.Tests.Storage;

/// <summary>
/// The app runs on EF Core's compiled model (Database/Concrete/CompiledModel, written by tools/compiled-model.ps1): it must be exactly the
/// model ConceptDbContext.OnModelCreating builds, or a mapping change would not reach the app. After a mapping change: run the tool.
/// </summary>
public sealed class CompiledModelTests
{
    [Fact]
    public async Task TheCompiledModelIsTheModelTheContextBuilds()
    {
        using var h = await StorageHarness.CreatedAsync();
        await h.QueryAsync(db =>
        {
            db.Model.ShouldBeOfType<ConceptDbContextModel>("the app uses the compiled model");

            // What queries and saves read from the model; the schema itself (column order, indexes) is created from the built model.
            Describe(db.Model).ShouldBe(Describe(db.GetService<IDesignTimeModel>().Model));
            return Task.FromResult(0);
        });
    }

    private static List<string> Describe(IModel model)
    {
        var lines = new List<string>();
        foreach (var entity in model.GetEntityTypes().OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            lines.Add($"{entity.Name} table {entity.GetTableName() ?? "-"} view {entity.GetViewName() ?? "-"}");
            lines.AddRange(entity.GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p =>
                $"{entity.Name}.{p.Name} {p.GetColumnName()} {p.ClrType.Name} {p.GetProviderClrType()?.Name ?? "-"} {p.IsNullable} " +
                $"{p.GetValueConverter()?.GetType().Name ?? "-"} {p.ValueGenerated}"));
            lines.AddRange(entity.GetKeys().Select(k => $"{entity.Name} key {string.Join(",", k.Properties.Select(p => p.Name))} {k.IsPrimaryKey()}"));
            lines.AddRange(entity.GetForeignKeys().Select(f =>
                $"{entity.Name} fk {string.Join(",", f.Properties.Select(p => p.Name))} {f.PrincipalEntityType.Name} {f.DeleteBehavior}").Order(StringComparer.Ordinal));
        }

        return lines;
    }
}
