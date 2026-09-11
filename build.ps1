<#
.SYNOPSIS
  Convenience build script for LUMEN.

.DESCRIPTION
  Wraps the local .NET 8 SDK (installed under %USERPROFILE%\.dotnet) so the repo
  builds even though the machine-wide dotnet has no SDK.

.PARAMETER Task
  restore | build | test | run | smoke | publish | release | clean   (default: build)

.EXAMPLE
  .\build.ps1 test
  .\build.ps1 run
#>
param(
    [ValidateSet('restore', 'build', 'test', 'run', 'smoke', 'crashtest', 'publish', 'release', 'clean')]
    [string]$Task = 'build',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot

# Prefer the user-local SDK.
$localDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $localDotnet 'dotnet.exe')) {
    $env:PATH = "$localDotnet;$env:PATH"
    $env:DOTNET_ROOT = $localDotnet
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_NOLOGO = 1

$sln = Join-Path $repo 'LUMEN.sln'
$game = Join-Path $repo 'src/LUMEN.Game/LUMEN.Game.csproj'

switch ($Task) {
    'restore' { dotnet restore $sln }
    'build'   { dotnet build $sln -c $Configuration }
    'test'    { dotnet test $sln -c $Configuration }
    'run'     { dotnet run --project $game -c $Configuration }
    'smoke'   { dotnet run --project $game -c $Configuration -- --smoke }
    'crashtest' { dotnet run --project $game -c $Configuration -- --crashtest }
    'clean'   {
        dotnet clean $sln -c $Configuration
        Get-ChildItem $repo -Include bin, obj -Recurse -Directory | Remove-Item -Recurse -Force
    }
    'publish' {
        # Native libraries are deliberately NOT self-extracted: MonoGame looks for SDL2.dll
        # next to its own assembly, and a single-file bundle has no such place, so bundling
        # them yields an executable that cannot start. See docs/RELEASE.md.
        dotnet publish $game -c Release -r win-x64 --self-contained `
            -p:PublishSingleFile=true `
            -o (Join-Path $repo 'publish')
    }
    'release' {
        # Every artifact, plus the verification pass over what it just built (§101).
        & (Join-Path $repo 'tools/release.ps1')
    }
}
