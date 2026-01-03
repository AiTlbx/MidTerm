#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates a new release by bumping version, committing, tagging, and pushing.

.PARAMETER Bump
    Version bump type: major, minor, or patch

.PARAMETER Message
    What was done in this release (used in commit message)

.PARAMETER InfluencesTtyHost
    MANDATORY: Does this release affect mthost or the protocol between mt and mthost?

    Answer 'yes' if ANY of these are true:
      - Changed Ai.Tlbx.MidTerm.TtyHost/ code
      - Changed Ai.Tlbx.MidTerm.Common/ (shared protocol code)
      - Changed mux WebSocket binary protocol format
      - Changed named pipe protocol between mt and mthost
      - Changed session ID encoding/format
      - Changed any IPC mechanism

    Answer 'no' if ONLY these changed:
      - TypeScript/frontend code
      - CSS/HTML
      - REST API endpoints (not used by mthost)
      - Web-only C# code (endpoints, auth, settings)

    When 'yes': Both mt and mthost versions bumped, terminals restart on update
    When 'no':  Only mt version bumped, terminals survive the update

.EXAMPLE
    .\release.ps1 -Bump patch -Message "Fix UI bug" -InfluencesTtyHost no
    # Web-only release - terminals survive the update

.EXAMPLE
    .\release.ps1 -Bump patch -Message "Fix PTY issue" -InfluencesTtyHost yes
    # Full release - terminals will be restarted
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("major", "minor", "patch")]
    [string]$Bump,

    [Parameter(Mandatory=$true)]
    [string]$Message,

    [Parameter(Mandatory=$true)]
    [ValidateSet("yes", "no")]
    [string]$InfluencesTtyHost
)

$ErrorActionPreference = "Stop"

# Files to update
$versionJsonPath = "$PSScriptRoot\version.json"
$webCsprojPath = "$PSScriptRoot\Ai.Tlbx.MidTerm\Ai.Tlbx.MidTerm.csproj"
$ttyHostCsprojPath = "$PSScriptRoot\Ai.Tlbx.MidTerm.TtyHost\Ai.Tlbx.MidTerm.TtyHost.csproj"
$ttyHostProgramPath = "$PSScriptRoot\Ai.Tlbx.MidTerm.TtyHost\Program.cs"

# Read current version from version.json
$versionJson = Get-Content $versionJsonPath | ConvertFrom-Json
$currentVersion = $versionJson.web
Write-Host "Current version: $currentVersion" -ForegroundColor Cyan

# Parse and bump version
$parts = $currentVersion.Split('.')
$major = [int]$parts[0]
$minor = [int]$parts[1]
$patch = [int]$parts[2]

switch ($Bump) {
    "major" { $major++; $minor = 0; $patch = 0 }
    "minor" { $minor++; $patch = 0 }
    "patch" { $patch++ }
}

$newVersion = "$major.$minor.$patch"
Write-Host "New version: $newVersion" -ForegroundColor Green

# Determine release type
$isPtyBreaking = $InfluencesTtyHost -eq "yes"
if ($isPtyBreaking) {
    Write-Host "Release type: FULL (mt + mthost)" -ForegroundColor Yellow
} else {
    Write-Host "Release type: Web-only (mt only, sessions preserved)" -ForegroundColor Green
}

# Update version.json
$versionJson.web = $newVersion
if ($isPtyBreaking) {
    $versionJson.pty = $newVersion
}
$versionJson | ConvertTo-Json | Set-Content $versionJsonPath
Write-Host "  Updated: version.json (web=$newVersion, pty=$($versionJson.pty))" -ForegroundColor Gray

# Update web csproj (use flexible regex to handle version mismatch)
$content = Get-Content $webCsprojPath -Raw
$content = $content -replace "<Version>\d+\.\d+\.\d+</Version>", "<Version>$newVersion</Version>"
Set-Content $webCsprojPath $content -NoNewline
Write-Host "  Updated: Ai.Tlbx.MidTerm.csproj" -ForegroundColor Gray

# Update TtyHost files only for PTY-breaking changes
if ($isPtyBreaking) {
    # Update TtyHost csproj
    $content = Get-Content $ttyHostCsprojPath -Raw
    $content = $content -replace "<Version>\d+\.\d+\.\d+</Version>", "<Version>$newVersion</Version>"
    $content = $content -replace "<FileVersion>\d+\.\d+\.\d+\.\d+</FileVersion>", "<FileVersion>$newVersion.0</FileVersion>"
    Set-Content $ttyHostCsprojPath $content -NoNewline
    Write-Host "  Updated: Ai.Tlbx.MidTerm.TtyHost.csproj" -ForegroundColor Gray

    # Update TtyHost Program.cs
    $content = Get-Content $ttyHostProgramPath -Raw
    $content = $content -replace 'public const string Version = "\d+\.\d+\.\d+"', "public const string Version = `"$newVersion`""
    Set-Content $ttyHostProgramPath $content -NoNewline
    Write-Host "  Updated: Ai.Tlbx.MidTerm.TtyHost\Program.cs" -ForegroundColor Gray
} else {
    Write-Host "  Skipped: TtyHost files (web-only release)" -ForegroundColor DarkGray
}

# Git operations
Write-Host ""
Write-Host "Committing and tagging..." -ForegroundColor Cyan

git add -A
if ($LASTEXITCODE -ne 0) { throw "git add failed" }

git commit -m "v${newVersion}: $Message"
if ($LASTEXITCODE -ne 0) { throw "git commit failed" }

git tag -a "v$newVersion" -m "v${newVersion}: $Message"
if ($LASTEXITCODE -ne 0) { throw "git tag failed" }

git push origin main
if ($LASTEXITCODE -ne 0) { throw "git push main failed" }

git push origin "v$newVersion"
if ($LASTEXITCODE -ne 0) { throw "git push tag failed" }

Write-Host ""
Write-Host "Released v$newVersion" -ForegroundColor Green
Write-Host "Monitor build: https://github.com/AiTlbx/MidTerm/actions" -ForegroundColor Cyan
