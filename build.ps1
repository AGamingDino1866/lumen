#Requires -Version 5.1
<#
    .SYNOPSIS
        One-command build: publish self-contained, verify the native gate, compile the
        installer, and drop Lumen-Setup-x.y.z.exe into dist/.

    .DESCRIPTION
        Every step here exists because it silently broke at least once during development:

        1. Publish is self-contained/single-file/ReadyToRun for win-x64. PublishTrimmed stays
           off (see Lumen.App.csproj) because WPF does not trim reliably.
        2. The native gate actually launches the published exe with a hidden --self-test-render
           switch (see App.xaml.cs) and renders a real page through it. Checking that pdfium.dll
           and libSkiaSharp.dll merely exist on disk is not enough: single-file publishing changes
           how native libraries are located at runtime, and `dotnet run` never exercises that
           path, so the only trustworthy check is running the actual published binary.
        3. Inno Setup's compiler is probed across every place it's plausible to find it,
           because on a per-user machine (no admin rights) it lands under
           %LOCALAPPDATA%\Programs, not Program Files.

    .PARAMETER Configuration
        Build configuration. Defaults to Release.

    .PARAMETER Runtime
        Target RID. Defaults to win-x64. win-arm64 is supported if you have that SDK pack.

    .PARAMETER SkipInstaller
        Publish and run the native gate, but skip compiling the Inno Setup installer. Useful for
        iterating on the app without Inno Setup installed.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Fail($message) {
    Write-Host ""
    Write-Host "BUILD FAILED: $message" -ForegroundColor Red
    exit 1
}

# ------------------------------------------------------------------------------------------
# 0. Version, read once from Directory.Build.props so the installer and the exe never drift.
# ------------------------------------------------------------------------------------------
[xml]$buildProps = Get-Content (Join-Path $repoRoot "Directory.Build.props")
$versionNode = $buildProps.SelectSingleNode("//Version")
if (-not $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    Fail "Could not read <Version> from Directory.Build.props."
}
$version = $versionNode.InnerText
Write-Host "Lumen version $version ($Runtime, $Configuration)"

# ------------------------------------------------------------------------------------------
# 1. Publish: self-contained, single-file, ReadyToRun. PublishTrimmed deliberately absent.
# ------------------------------------------------------------------------------------------
Write-Step "Publishing Lumen.App ($Runtime, self-contained, single-file, ReadyToRun)"

$appProject = Join-Path $repoRoot "src\Lumen.App\Lumen.App.csproj"
$publishDir = Join-Path $repoRoot "src\Lumen.App\bin\$Configuration\net8.0-windows\$Runtime\publish"

dotnet publish $appProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) {
    Fail "dotnet publish exited with code $LASTEXITCODE."
}

$exePath = Join-Path $publishDir "Lumen.exe"
if (-not (Test-Path $exePath)) {
    Fail "Publish reported success but Lumen.exe is missing from $publishDir."
}

$payloadBytes = (Get-Item $exePath).Length
$payloadMb = [Math]::Round($payloadBytes / 1MB, 1)
Write-Host "Published: $exePath ($payloadMb MB)"

# ------------------------------------------------------------------------------------------
# 2. The native gate. Single-file publishing extracts native libraries at first launch (via
#    IncludeNativeLibrariesForSelfExtract) rather than shipping them as loose sibling files, so
#    presence on disk right after publish is not itself meaningful — what matters is that the
#    published exe can actually load and use them. --self-test-render proves that directly.
# ------------------------------------------------------------------------------------------
Write-Step "Native gate: launching the published exe and rendering a real PDF page"

$fixturePdf = Join-Path $repoRoot "tests\Lumen.Core.Tests\Fixtures\sample.pdf"
if (-not (Test-Path $fixturePdf)) {
    Fail "Native gate fixture missing: $fixturePdf. This ships in the repo; do not delete it."
}

$resultFile = Join-Path $publishDir "self-test-result.txt"
Remove-Item $resultFile -ErrorAction SilentlyContinue

$selfTest = Start-Process -FilePath $exePath `
    -ArgumentList @("--self-test-render", "`"$fixturePdf`"") `
    -Wait -PassThru -WindowStyle Hidden

$resultText = if (Test-Path $resultFile) { Get-Content $resultFile -Raw } else { "(no result file was written)" }

if ($selfTest.ExitCode -ne 0) {
    Fail @"
The published exe could not render a page through the real single-file native path.
Exit code: $($selfTest.ExitCode)
Result:    $resultText

This almost always means pdfium.dll or libSkiaSharp.dll failed to load or extract from the
single-file bundle. Re-run without -p:PublishSingleFile=true to confirm the app itself is fine,
then check that the PDFtoImage / bblanchon.PDFium.Win32 package versions in
Directory.Packages.props still ship native assets for $Runtime.
"@
}

Write-Host "Native gate passed: $resultText" -ForegroundColor Green

# ------------------------------------------------------------------------------------------
# 3. Locate the Inno Setup compiler. On a per-user, no-admin install (the common case for
#    developers on managed machines) it lands under %LOCALAPPDATA%\Programs, not Program Files.
# ------------------------------------------------------------------------------------------
$distDir = Join-Path $repoRoot "dist"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

if ($SkipInstaller) {
    Write-Host ""
    Write-Host "Skipping installer (-SkipInstaller). Publish output: $publishDir" -ForegroundColor Yellow
    exit 0
}

Write-Step "Locating the Inno Setup 6 compiler (iscc.exe)"

function Find-Iscc {
    $onPath = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    $candidates = @(
        (Join-Path $env:ProgramFiles "Inno Setup 6\iscc.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\iscc.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\iscc.exe")
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) {
            return $candidate
        }
    }

    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    Fail @"
Inno Setup 6's compiler (iscc.exe) was not found on PATH, in Program Files, in
Program Files (x86), or under %LOCALAPPDATA%\Programs.

Install it from: https://jrsoftware.org/isdl.php

Or re-run with -SkipInstaller to publish without building the installer:
    .\build.ps1 -SkipInstaller
"@
}
Write-Host "Found: $iscc"

# ------------------------------------------------------------------------------------------
# 4. Compile the installer. Lumen.iss reads the publish output and version via /D switches so
#    the .iss file itself never hardcodes a path or a version number.
# ------------------------------------------------------------------------------------------
Write-Step "Compiling the installer"

$issPath = Join-Path $repoRoot "installer\Lumen.iss"
if (-not (Test-Path $issPath)) {
    Fail "Missing installer script: $issPath"
}

& $iscc `
    "/DLumenVersion=$version" `
    "/DLumenPublishDir=$publishDir" `
    "/DLumenSourceRoot=$repoRoot" `
    "/O$distDir" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    Fail "iscc.exe exited with code $LASTEXITCODE."
}

$setupExe = Join-Path $distDir "Lumen-Setup-$version.exe"
if (-not (Test-Path $setupExe)) {
    Fail "iscc reported success but $setupExe was not produced. Check OutputBaseFilename in Lumen.iss."
}

$setupMb = [Math]::Round((Get-Item $setupExe).Length / 1MB, 1)
Write-Host ""
Write-Host "Built: $setupExe ($setupMb MB)" -ForegroundColor Green

# ------------------------------------------------------------------------------------------
# 5. Code signing. No certificate exists yet (see README's SmartScreen section). When one does,
#    uncomment and point SignTool at it — signtool.exe ships with the Windows SDK.
# ------------------------------------------------------------------------------------------
# $certThumbprint = $env:LUMEN_SIGNING_THUMBPRINT
# if ($certThumbprint) {
#     Write-Step "Signing $setupExe"
#     & signtool.exe sign /sha1 $certThumbprint /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $setupExe
#     if ($LASTEXITCODE -ne 0) { Fail "signtool.exe exited with code $LASTEXITCODE." }
# }
