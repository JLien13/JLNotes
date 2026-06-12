# installer/build.ps1 -- JL Notes installer builder.
#
# Two phases:
#   1. dotnet publish -> publish\  (framework-dependent, win-x64)
#   2. ISCC.exe       -> installer\Output\JLNotes-Setup-<version>.exe
#
# Version is the single source of truth in src\JLNotes\JLNotes.csproj (<Version>);
# pass -Version to override.
#
# Usage:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1
#   pwsh -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1 -Version 1.1.0
#   pwsh -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1 -SkipPublish

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot  = Split-Path -Parent $PSScriptRoot
$Csproj    = Join-Path $RepoRoot "src\JLNotes\JLNotes.csproj"
$PublishDir = Join-Path $RepoRoot "publish"
$Iss       = Join-Path $PSScriptRoot "JLNotes.iss"

# Resolve version from the csproj <Version> element unless given.
if (-not $Version) {
    if (-not (Test-Path $Csproj)) { throw "csproj not found at $Csproj -- pass -Version <X.Y.Z>." }
    $m = Select-String -Path $Csproj -Pattern '<Version>\s*([^<]+?)\s*</Version>' | Select-Object -First 1
    if ($m) { $Version = $m.Matches[0].Groups[1].Value } else { throw "No <Version> in $Csproj -- pass -Version <X.Y.Z>." }
}
Write-Host "==> Building JL Notes installer, version $Version" -ForegroundColor Cyan

# Phase 1: dotnet publish (matches README: framework-dependent, self-contained false).
if (-not $SkipPublish) {
    Write-Host "==> dotnet publish" -ForegroundColor Cyan
    & dotnet publish $Csproj -c Release -r win-x64 --self-contained false -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
} else {
    Write-Host "==> Skipping publish (per -SkipPublish)" -ForegroundColor DarkGray
}
if (-not (Test-Path (Join-Path $PublishDir "JLNotes.exe"))) {
    throw "publish\JLNotes.exe not found. Run a publish first (drop -SkipPublish)."
}

# Phase 2: ISCC. This PC keeps Inno Setup as a per-user install under LOCALAPPDATA.
$IsccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
)
$Iscc = $IsccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $Iscc = $cmd.Source }
}
if (-not $Iscc) { throw "ISCC.exe not found. Install Inno Setup 6: winget install JRSoftware.InnoSetup" }

$OutputDir = Join-Path $PSScriptRoot "Output"
Write-Host "==> Compiling installer with $Iscc" -ForegroundColor Cyan
& $Iscc "/Qp" "/DMyAppVersion=$Version" "/O$OutputDir" $Iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$Setup = Join-Path $OutputDir "JLNotes-Setup-$Version.exe"
if (Test-Path $Setup) {
    Write-Host ""
    Write-Host "==> Done: $Setup" -ForegroundColor Green
    Get-Item $Setup | Format-Table Name, Length, LastWriteTime
} else {
    Write-Warning "Installer not found at expected path: $Setup"
}
