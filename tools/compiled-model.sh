#!/usr/bin/env sh
# Writes the EF Core compiled model of the storage again (src/TMConcept.Storage/Database/Concrete/CompiledModel). Run it after any change
# to ConceptDbContext's mapping; the test CompiledModelTests fails until then. Usage: tools/compiled-model.sh
#
# The compiled model saves EF Core building its model at every start (measured in a trimmed single-file build: about 1 s less at the unlock).
# dotnet-ef needs a startup project that references Microsoft.EntityFrameworkCore.Design; that package and the design-time factory stay out
# of the repository: both live in a temporary folder that is removed afterwards. Nothing is sent anywhere but to NuGet (the tool, the package).
set -eu
root=$(cd "$(dirname "$0")/.." && pwd)
ef_version=$(sed -n 's:.*"Microsoft.EntityFrameworkCore.Sqlite.Core" Version="\([^"]*\)".*:\1:p' "$root/Directory.Packages.props" | head -n 1)
storage="$root/src/TMConcept.Storage/TMConcept.Storage.csproj"
output="$root/src/TMConcept.Storage/Database/Concrete/CompiledModel"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
dotnet tool install dotnet-ef --version "$ef_version" --tool-path "$work/tool"
# The assembly is named like the tests so it sees the storage's internal context (InternalsVisibleTo).
cat > "$work/design.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TMConcept.Tests</AssemblyName>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$storage" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="$ef_version" />
  </ItemGroup>
</Project>
EOF
cat > "$work/Program.cs" <<'EOF'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TMConcept.Storage.Database;

internal sealed class DesignTimeFactory : IDesignTimeDbContextFactory<ConceptDbContext>
{
    public ConceptDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ConceptDbContext>().UseSqlite("Data Source=:memory:").Options);
}

internal static class Program
{
    private static void Main()
    {
    }
}
EOF
dotnet build "$work/design.csproj" -nologo
mkdir -p "$output"
find "$output" -maxdepth 1 -name '*.cs' -exec rm -f {} +
"$work/tool/dotnet-ef" dbcontext optimize --project "$storage" --startup-project "$work/design.csproj" \
  --context ConceptDbContext --output-dir "$output" --namespace TMConcept.Storage.Database --no-color
# The repository stores LF line endings and no byte order mark (.gitattributes); dotnet-ef writes CRLF with one.
for file in "$output"/*.cs; do
  sed -i -e '1s/^\xEF\xBB\xBF//' -e 's/\r$//' "$file"
done
echo "Compiled model written to $output"
