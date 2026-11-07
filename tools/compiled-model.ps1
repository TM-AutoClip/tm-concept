# Writes the EF Core compiled model of the storage again (src/TMConcept.Storage/Database/Concrete/CompiledModel). Run it after any change
# to ConceptDbContext's mapping; the test CompiledModelTests fails until then. Usage: tools/compiled-model.ps1
#
# The compiled model saves EF Core building its model at every start (measured in a trimmed single-file build: about 1 s less at the unlock).
# dotnet-ef needs a startup project that references Microsoft.EntityFrameworkCore.Design; that package and the design-time factory stay out
# of the repository: both live in a temporary folder that is removed afterwards. Nothing is sent anywhere but to NuGet (the tool, the package).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$packages = Get-Content (Join-Path $root 'Directory.Packages.props')
$efVersion = ($packages.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'Microsoft.EntityFrameworkCore.Sqlite.Core' }).Version
$storage = Join-Path $root 'src/TMConcept.Storage/TMConcept.Storage.csproj'
$output = Join-Path $root 'src/TMConcept.Storage/Database/Concrete/CompiledModel'
$work = Join-Path ([IO.Path]::GetTempPath()) ('tmconcept-compiled-model-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    & dotnet tool install dotnet-ef --version $efVersion --tool-path (Join-Path $work 'tool')
    if ($LASTEXITCODE -ne 0) { throw 'dotnet-ef could not be installed' }
    # The assembly is named like the tests so it sees the storage's internal context (InternalsVisibleTo).
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>TMConcept.Tests</AssemblyName>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$storage" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="$efVersion" />
  </ItemGroup>
</Project>
"@ | Set-Content -Encoding utf8 (Join-Path $work 'design.csproj')
    @'
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
'@ | Set-Content -Encoding utf8 (Join-Path $work 'Program.cs')
    & dotnet build (Join-Path $work 'design.csproj') -nologo
    if ($LASTEXITCODE -ne 0) { throw 'The design-time project did not build' }
    if (Test-Path $output) { Get-ChildItem $output -Filter *.cs | Remove-Item }
    & (Join-Path $work 'tool/dotnet-ef') dbcontext optimize --project $storage --startup-project (Join-Path $work 'design.csproj') `
        --context ConceptDbContext --output-dir $output --namespace TMConcept.Storage.Database --no-color
    if ($LASTEXITCODE -ne 0) { throw 'dotnet ef dbcontext optimize failed' }
    # The repository stores LF line endings and no byte order mark (.gitattributes); dotnet-ef writes CRLF with one.
    foreach ($file in Get-ChildItem $output -Filter *.cs) {
        $text = [IO.File]::ReadAllText($file.FullName) -replace "`r`n", "`n"
        [IO.File]::WriteAllText($file.FullName, $text, (New-Object Text.UTF8Encoding $false))
    }
    Write-Output "Compiled model written to $output"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
