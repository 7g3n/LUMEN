<#
.SYNOPSIS
  Builds every LUMEN release artifact and verifies them (spec §72-73, §101).

.DESCRIPTION
  One command, three artifacts, and a verification pass over what it just built:

    dist\LUMEN-<version>-win-x64\   the published game
    dist\LUMEN-Portable-<version>.zip
    dist\LUMEN-Setup-<version>.exe  (when Inno Setup is installed)

  The verification is the point as much as the build is. An installer that compiles is
  not evidence of anything; an executable that starts, plays a chart end to end, catches
  a deliberate crash, and writes its data where it promised to, is. So this script runs
  the artifact it produced rather than the one in bin\, and fails if any check fails.

  Nothing here touches the network. The publish is self-contained, so the machine it
  lands on needs no .NET install, and the checks below run with no connection of any kind.

.PARAMETER Task
  all | publish | portable | installer | verify | clean    (default: all)

.PARAMETER SkipVerify
  Build the artifacts without running the verification pass.

.EXAMPLE
  .\tools\release.ps1
  .\tools\release.ps1 verify
#>
param(
    [ValidateSet('all', 'publish', 'portable', 'installer', 'verify', 'clean')]
    [string]$Task = 'all',

    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = Split-Path -Parent $PSScriptRoot
$game = Join-Path $repo 'src\LUMEN.Game\LUMEN.Game.csproj'
$sln = Join-Path $repo 'LUMEN.sln'
$dist = Join-Path $repo 'dist'

# Prefer the user-local SDK, as build.ps1 does: the machine-wide dotnet has no SDK.
$localDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $localDotnet 'dotnet.exe')) {
    $env:PATH = "$localDotnet;$env:PATH"
    $env:DOTNET_ROOT = $localDotnet
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_NOLOGO = 1

function Write-Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Pass($text) { Write-Host "  [pass] $text" -ForegroundColor Green }
function Write-Skip($text) { Write-Host "  [skip] $text" -ForegroundColor Yellow }

$script:Failures = @()
function Write-Fail($text) {
    Write-Host "  [FAIL] $text" -ForegroundColor Red
    $script:Failures += $text
}

# --- version -----------------------------------------------------------------
# One source, the same one the assemblies are stamped with (see GameIdentity.cs).

function Get-Version {
    [xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
    $version = $props.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw 'No <Version> in Directory.Build.props' }
    return $version.Trim()
}

$version = Get-Version
$appDir = Join-Path $dist "LUMEN-$version-win-x64"
$zipPath = Join-Path $dist "LUMEN-Portable-$version.zip"
$setupPath = Join-Path $dist "LUMEN-Setup-$version.exe"

# --- publish -----------------------------------------------------------------

function Invoke-Publish {
    Write-Step "Publishing LUMEN $version (self-contained, win-x64)"

    if (Test-Path $appDir) { Remove-Item $appDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $appDir | Out-Null

    # ReadyToRun comes from the csproj for Release. Native libraries are left beside the
    # executable rather than bundled into it: MonoGame resolves SDL2 by looking next to
    # the assembly, and inside a single-file bundle there is no such place, so bundling
    # them produces an executable that cannot start. Three DLLs beside the game is the
    # honest shape of this application.
    dotnet publish $game `
        -c Release -r win-x64 --self-contained `
        -p:PublishSingleFile=true `
        -o $appDir
    if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

    $size = (Get-ChildItem $appDir -Recurse -File | Measure-Object -Property Length -Sum).Sum
    Write-Host ("  {0} -> {1:N0} bytes ({2:N0} MB)" -f $appDir, $size, ($size / 1MB))
}

# --- portable zip ------------------------------------------------------------

function Invoke-Portable {
    Write-Step "Building $([System.IO.Path]::GetFileName($zipPath))"

    if (-not (Test-Path $appDir)) { throw "Nothing published at $appDir - run 'publish' first" }

    # The sentinel is what makes it portable: with it beside the executable, LUMEN keeps
    # its data in .\data instead of %LOCALAPPDATA%, so the whole thing travels on a stick
    # and leaves nothing behind on the machine it ran on (§73).
    $stage = Join-Path $dist "portable-stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    Copy-Item (Join-Path $appDir '*') $stage -Recurse -Force
    Set-Content -Path (Join-Path $stage 'portable.txt') -Encoding utf8 -Value @(
        'This file makes LUMEN portable.',
        '',
        'While it sits beside LUMEN.exe, everything LUMEN saves - your profile, scores,',
        'rating, charts, songs, replays and settings - goes into the data folder next to',
        'the executable instead of %LOCALAPPDATA%\LUMEN.',
        '',
        'Delete it to use the normal location instead.'
    )

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Remove-Item $stage -Recurse -Force

    $size = (Get-Item $zipPath).Length
    Write-Host ("  {0} -> {1:N0} bytes ({2:N0} MB)" -f $zipPath, $size, ($size / 1MB))
}

# --- installer ---------------------------------------------------------------

function Find-Iscc {
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    # Inno Setup installs per-machine or per-user depending on how it was run, and winget
    # picks the per-user location — which is not under Program Files at all. Looking only
    # there is how this script first reported "not installed" about a copy that was.
    $roots = @(
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)},
        (Join-Path $env:LOCALAPPDATA 'Programs'),
        (Join-Path $env:ProgramData 'chocolatey\lib\innosetup\tools')
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $roots) {
        foreach ($version in @('Inno Setup 6', 'Inno Setup 5', '')) {
            $candidate = Join-Path (Join-Path $root $version) 'ISCC.exe'
            if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
        }
    }

    return $null
}

function Invoke-Installer {
    Write-Step "Building $([System.IO.Path]::GetFileName($setupPath))"

    if (-not (Test-Path $appDir)) { throw "Nothing published at $appDir - run 'publish' first" }

    $iscc = Find-Iscc
    if (-not $iscc) {
        # Not a build failure: the other two artifacts are complete and usable. It is
        # reported loudly because a release is not a release without all three.
        Write-Skip 'Inno Setup (ISCC.exe) not found - installer not built.'
        Write-Host '         Install it with:  winget install --id JRSoftware.InnoSetup' -ForegroundColor Yellow
        Write-Host '         then re-run:      .\tools\release.ps1 installer' -ForegroundColor Yellow
        return $false
    }

    $script = Join-Path $repo 'tools\installer\LUMEN.iss'
    & $iscc "/DAppVersion=$version" "/DSourceDir=$appDir" "/DOutputDir=$dist" $script
    if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

    if (-not (Test-Path $setupPath)) { throw "ISCC reported success but $setupPath is missing" }

    $size = (Get-Item $setupPath).Length
    Write-Host ("  {0} -> {1:N0} bytes ({2:N0} MB)" -f $setupPath, $size, ($size / 1MB))
    return $true
}

# --- verification (§101) -----------------------------------------------------

function Invoke-Exe($exe, $arguments, $timeoutSeconds = 180) {
    $stdout = [System.IO.Path]::GetTempFileName()
    $stderr = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -NoNewWindow `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr

        # Touching Handle caches it on the object. Without this, Start-Process -PassThru
        # hands back a Process whose ExitCode reads as empty once the process has gone,
        # and every check here would then fail a run that in fact succeeded — which is the
        # worst kind of verification, one that cries wolf.
        $null = $p.Handle

        if (-not $p.WaitForExit($timeoutSeconds * 1000)) {
            $p.Kill()
            return [pscustomobject]@{ ExitCode = -1; Output = "(timed out after ${timeoutSeconds}s)" }
        }

        # The timed overload returns as soon as the process ends; the parameterless call is
        # what settles the process object and its redirected output.
        $p.WaitForExit()

        $text = (Get-Content $stdout -Raw) + (Get-Content $stderr -Raw)
        return [pscustomobject]@{ ExitCode = $p.ExitCode; Output = $text }
    }
    finally {
        Remove-Item $stdout, $stderr -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-Verify {
    Write-Step "Verifying the built artifacts (§101)"

    if (-not (Test-Path $appDir)) { throw "Nothing published at $appDir - run 'publish' first" }

    $exe = Join-Path $appDir 'LUMEN.exe'

    # 1. What shipped is what the game needs, and nothing it does not.
    if (Test-Path $exe) { Write-Pass 'LUMEN.exe is present' } else { Write-Fail 'LUMEN.exe is missing' }

    foreach ($native in @('SDL2.dll', 'soft_oal.dll', 'e_sqlite3.dll')) {
        if (Test-Path (Join-Path $appDir $native)) {
            Write-Pass "$native ships beside the game"
        }
        else {
            Write-Fail "$native is missing - the game will not start without it"
        }
    }

    $fonts = Get-ChildItem (Join-Path $appDir 'assets\fonts') -Filter '*.ttf' -ErrorAction SilentlyContinue
    if ($fonts -and $fonts.Count -ge 3) {
        Write-Pass "$($fonts.Count) bundled fonts ship with the game"
    }
    else {
        Write-Fail 'The bundled fonts are missing - every screen would render without text'
    }

    # WPF and WinForms have no business in this application; if they reappear, something
    # has pulled the whole desktop runtime back in and the download has doubled.
    $desktop = Get-ChildItem $appDir -Filter '*_cor3.dll' -ErrorAction SilentlyContinue
    if ($desktop) {
        Write-Fail "The Windows Desktop runtime is being shipped again: $($desktop.Name -join ', ')"
    }
    else {
        Write-Pass 'No Windows Desktop (WPF/WinForms) runtime is shipped'
    }

    # 2. No .NET install required: a self-contained publish carries its own runtime.
    if (Test-Path (Join-Path $appDir 'LUMEN.runtimeconfig.json')) {
        Write-Fail 'A runtimeconfig.json beside the exe means the publish is not self-contained'
    }
    else {
        Write-Pass 'Self-contained: no runtimeconfig beside the executable, no .NET install needed'
    }

    # 3. It starts, says which build it is, and puts its data where it promised.
    #
    #    Only --smoke runs against the real %LOCALAPPDATA%: it reaches the setup screen
    #    and exits, creating the folder tree and an empty database and nothing else.
    #    Everything that would write a profile, a score or a replay runs in the portable
    #    probe below instead — a release check has no business leaving a play in somebody's
    #    own game data.
    $smoke = Invoke-Exe $exe '--smoke' 120
    if ($smoke.ExitCode -eq 0 -and $smoke.Output -match 'smoke ok') {
        $line = ($smoke.Output -split "`n" | Where-Object { $_ -match 'smoke ok' } | Select-Object -First 1).Trim()
        Write-Pass "Starts and renders: $line"
    }
    else {
        Write-Fail "--smoke failed (exit $($smoke.ExitCode)): $($smoke.Output)"
    }

    if ($smoke.Output -match "LUMEN\s+$([regex]::Escape($version))") {
        Write-Pass "Reports version $version"
    }
    else {
        Write-Fail "The executable does not report version $version"
    }

    $localData = Join-Path $env:LOCALAPPDATA 'LUMEN'
    if (Test-Path (Join-Path $localData 'database\lumen.db')) {
        Write-Pass 'User data lives under %LOCALAPPDATA%\LUMEN'
    }
    else {
        Write-Fail 'No database under %LOCALAPPDATA%\LUMEN after running the game'
    }

    if (Test-Path (Join-Path $appDir 'data')) {
        Write-Fail 'The game wrote a data folder into its own install directory'
    }
    else {
        Write-Pass 'Nothing was written into the install folder'
    }

    # 4-7. Everything that plays, scores or crashes, in a throwaway portable copy. This
    #      doubles as the check that the portable sentinel works at all (§73), since the
    #      data it writes has to land beside the executable rather than in %LOCALAPPDATA%.
    $probe = Join-Path $dist 'verify-portable'
    if (Test-Path $probe) { Remove-Item $probe -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $probe | Out-Null

    try {
        Copy-Item (Join-Path $appDir '*') $probe -Recurse -Force
        Set-Content -Path (Join-Path $probe 'portable.txt') -Value '' -Encoding utf8
        $probeExe = Join-Path $probe 'LUMEN.exe'

        $auto = Invoke-Exe $probeExe '--autoplay' 300

        if (Test-Path (Join-Path $probe 'data\database\lumen.db')) {
            Write-Pass 'With portable.txt beside it, the game keeps its data next to the executable'
        }
        else {
            Write-Fail 'The portable sentinel did not move the data folder'
        }

        if ($auto.ExitCode -eq 0 -and $auto.Output -match 'autoplay done: 100\.00%') {
            Write-Pass 'Plays the practice chart end to end and scores it'
        }
        else {
            Write-Fail "--autoplay failed (exit $($auto.ExitCode)): $($auto.Output)"
        }

        if ($auto.Output -match "the game's own (\d+)") {
            $own = [int]$Matches[1]
            if ($own -eq 0) {
                Write-Pass "No frame over budget from the game's own work"
            }
            else {
                Write-Fail "$own frame(s) over budget from the game's own work"
            }
        }
        else {
            Write-Fail 'No frame-time figures in the autoplay output'
        }

        # The song it just played is generated on first run, which is what lets a machine
        # that has never seen LUMEN have something to play (§81).
        if (Test-Path (Join-Path $probe 'data\songs\lumen-practice.wav')) {
            Write-Pass 'The practice track is generated on a fresh install'
        }
        else {
            Write-Fail 'No practice track on a fresh install - a new player would have nothing to play'
        }

        $crash = Invoke-Exe $probeExe '--crashtest' 120
        if ($crash.Output -match 'fatal handled') {
            Write-Pass 'A deliberate crash is caught and a report written'
        }
        else {
            Write-Fail "--crashtest did not produce a handled fatal: $($crash.Output)"
        }

        $reports = Get-ChildItem (Join-Path $probe 'data\logs') -Filter 'crash-*.txt' -ErrorAction SilentlyContinue
        if ($reports) {
            $body = Get-Content $reports[0].FullName -Raw
            if ($body -match [regex]::Escape($version)) {
                Write-Pass 'The crash report names the build it came from'
            }
            else {
                Write-Fail 'The crash report does not name the build'
            }
        }
        else {
            Write-Fail 'No crash report was written'
        }
    }
    finally {
        Remove-Item $probe -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 8. The artifacts themselves.
    if (Test-Path $zipPath) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $names = $zip.Entries | ForEach-Object { $_.FullName }
            if ($names -contains 'portable.txt') {
                Write-Pass 'The portable zip carries its sentinel'
            }
            else {
                Write-Fail 'The portable zip has no portable.txt, so it is not portable'
            }

            if ($names -contains 'LUMEN.exe') {
                Write-Pass 'The portable zip carries the game'
            }
            else {
                Write-Fail 'The portable zip has no LUMEN.exe'
            }
        }
        finally { $zip.Dispose() }
    }
    else {
        Write-Skip 'No portable zip to check'
    }

    if (Test-Path $setupPath) {
        Write-Pass "Installer built: $([System.IO.Path]::GetFileName($setupPath))"
    }
    else {
        Write-Skip 'No installer to check (Inno Setup not installed)'
    }
}

# --- tasks -------------------------------------------------------------------

New-Item -ItemType Directory -Force -Path $dist | Out-Null

switch ($Task) {
    'clean' {
        if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
        Write-Host "Removed $dist"
    }
    'publish' { Invoke-Publish }
    'portable' { Invoke-Portable }
    'installer' { [void](Invoke-Installer) }
    'verify' { Invoke-Verify }
    'all' {
        Write-Step "Running the test suite first"
        dotnet test $sln -c Release --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'tests failed - not building a release from a red suite' }

        Invoke-Publish
        Invoke-Portable
        [void](Invoke-Installer)
        if (-not $SkipVerify) { Invoke-Verify }
    }
}

if ($script:Failures.Count -gt 0) {
    Write-Host "`n$($script:Failures.Count) check(s) failed." -ForegroundColor Red
    exit 1
}

if ($Task -in @('all', 'verify')) {
    Write-Host "`nAll checks passed." -ForegroundColor Green
}
