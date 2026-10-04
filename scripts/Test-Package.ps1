param(
    [Parameter(Mandatory = $true)][string]$WorkDirectory,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')][string]$PackageVersion,
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json',
    [switch]$PublishAot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runId = [Guid]::NewGuid().ToString('N')
$work = Join-Path ([System.IO.Path]::GetFullPath($WorkDirectory)) "run-$runId"
$feed = Join-Path $work 'feed'
$version = if ($PackageVersion) { $PackageVersion } else { '2.5.0-smoke.' + $runId }
New-Item -ItemType Directory -Force -Path $feed | Out-Null

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $($Arguments -join ' ')" }
}

if (!$PackageVersion) {
    Push-Location $root
    try {
        Invoke-Dotnet -Arguments @('pack', 'src/PropertyResolvers.Attributes/PropertyResolvers.Attributes.csproj', '-c', 'Release', '-o', $feed, "-p:PackageVersion=$version")
        Invoke-Dotnet -Arguments @('pack', 'src/PropertyResolvers.Generators/PropertyResolvers.Generators.csproj', '-c', 'Release', '-o', $feed, "-p:PackageVersion=$version")
    } finally { Pop-Location }
}

$sdks = & dotnet --list-sdks
$sdk8 = ($sdks | Where-Object { $_ -match '^8\.0\.4[0-9][0-9]\s' } | Select-Object -Last 1) -split ' ' | Select-Object -First 1
if (!$sdk8) { throw 'Install a .NET 8.0.4xx SDK to test the minimum supported Roslyn compiler.' }
$propertySource = if ($PackageVersion) { 'packages' } else { 'local' }
$frameworkSource = if ($PackageSource -eq 'https://api.nuget.org/v3/index.json') { 'packages' } else { 'nuget' }
$frameworkSourceDeclaration = if ($frameworkSource -eq 'nuget') { '<add key="nuget" value="https://api.nuget.org/v3/index.json"/>' } else { '' }
$sourceMapping = if ($propertySource -eq $frameworkSource) {
    "<packageSource key=`"$propertySource`"><package pattern=`"*`"/></packageSource>"
} else {
    "<packageSource key=`"$propertySource`"><package pattern=`"PropertyResolvers`"/><package pattern=`"PropertyResolvers.Attributes`"/></packageSource><packageSource key=`"$frameworkSource`"><package pattern=`"*`"/></packageSource>"
}
$nugetConfig = @"
<configuration>
  <packageSources><clear/><add key="local" value="$([System.Security.SecurityElement]::Escape($feed))"/><add key="packages" value="$([System.Security.SecurityElement]::Escape($PackageSource))"/>$frameworkSourceDeclaration</packageSources>
  <packageSourceMapping>
    $sourceMapping
  </packageSourceMapping>
  <config><add key="globalPackagesFolder" value="$([System.Security.SecurityElement]::Escape((Join-Path $work 'packages')))"/></config>
</configuration>
"@
Set-Content (Join-Path $work 'NuGet.Config') $nugetConfig

$program = @'
using System;
using PropertyResolvers.Attributes;

[assembly: GeneratePropertyResolver("AccountId", Aliases = new[] { "AccountNumber" }, IncludeNamespaces = new[] { "Models" })]
[assembly: GeneratePropertyResolver("Customer.Amount", IncludeNamespaces = new[] { "Models" })]

public static class Program
{
    public static void Main()
    {
        var id = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        if (Smoke.Generated.AccountIdResolver.GetAccountId(new Models.Legacy { AccountNumber = id }) != id)
            throw new Exception("Typed alias resolution failed");
        if (!Smoke.Generated.CustomerAmountResolver.TryGet(new Models.Entity(), out decimal? amount) || amount != null)
            throw new Exception("Null path resolution failed");
        if (!Smoke.Generated.PropertyResolverDispatch.TryResolve("accountid", new Models.Entity { AccountId = id }, out var value) || value != id.ToString())
            throw new Exception("Static dispatch failed");
        if (Smoke.Generated.PropertyResolverDispatch.TryResolve("AccountId", new object(), out _))
            throw new Exception("Unsupported source was matched");
        Console.WriteLine("Package smoke test passed");
    }
}
namespace Models
{
    public class Entity { public Guid AccountId { get; set; } public Customer? Customer { get; set; } }
    public class Legacy { public Guid AccountNumber { get; set; } }
    public class Customer { public decimal Amount { get; set; } }
}
'@

foreach ($framework in @('net8.0', 'net10.0', 'netstandard2.0')) {
    $consumer = Join-Path $work $framework
    New-Item -ItemType Directory -Force -Path $consumer | Out-Null
    if ($framework -eq 'net10.0') {
        Copy-Item (Join-Path $root 'global.json') (Join-Path $consumer 'global.json')
    } else {
        Set-Content (Join-Path $consumer 'global.json') "{`"sdk`":{`"version`":`"$sdk8`",`"rollForward`":`"latestPatch`"}}"
    }
    $outputType = if ($framework -eq 'netstandard2.0') { 'Library' } else { 'Exe' }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$framework</TargetFramework><OutputType>$outputType</OutputType>
    <LangVersion>10.0</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <PropertyResolversNamespace>Smoke.Generated</PropertyResolversNamespace>
    <PropertyResolversOutput>Typed</PropertyResolversOutput>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="PropertyResolvers" Version="$version"/></ItemGroup>
</Project>
"@
    Set-Content (Join-Path $consumer 'Consumer.csproj') $project
    Set-Content (Join-Path $consumer 'Program.cs') $program
    if ($framework -eq 'netstandard2.0') {
        # Also exercise the legacy registration shim on an older reference surface.
        Set-Content (Join-Path $consumer 'Legacy.cs') @'
[assembly: PropertyResolvers.Attributes.GeneratePropertyResolver("TenantId", Output = PropertyResolvers.Attributes.ResolverOutput.String, RegisterRuntime = true, IncludeNamespaces = new[] { "LegacyModels" })]
namespace LegacyModels { public class Tenant { public int TenantId => 123; } }
'@
    }
    Push-Location $consumer
    try {
        Invoke-Dotnet -Arguments @('restore', '--configfile', (Join-Path $work 'NuGet.Config'))
        Invoke-Dotnet -Arguments @('build', '-c', 'Release', '--no-restore')
        if ($outputType -eq 'Exe') { Invoke-Dotnet -Arguments @('run', '-c', 'Release', '--no-build') }
        if ($PublishAot -and $framework -eq 'net8.0') {
            $rid = [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
            Invoke-Dotnet -Arguments @('publish', '-c', 'Release', '-r', $rid, '--self-contained', 'true', '-p:PublishAot=true', '-p:TrimmerSingleWarn=false')
            $executable = Join-Path $consumer "bin/Release/$framework/$rid/publish/Consumer"
            if ($IsWindows) { $executable += '.exe' }
            & $executable
            if ($LASTEXITCODE -ne 0) { throw 'Native AOT consumer failed' }
        }
    } finally { Pop-Location }
}
Write-Host "Package validation complete: $work"
