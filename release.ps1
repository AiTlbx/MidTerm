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

.PARAMETER Details
    Optional array of bullet points for richer changelog.
    Each item becomes a "- item" line in the commit message.

.EXAMPLE
    .\release.ps1 -Bump patch -Message "Fix UI bug" -InfluencesTtyHost no
    # Web-only release - terminals survive the update

.EXAMPLE
    .\release.ps1 -Bump patch -Message "Fix PTY issue" -InfluencesTtyHost yes
    # Full release - terminals will be restarted

.EXAMPLE
    .\release.ps1 -Bump minor -Message "Memory efficiency improvements" -Details @(
        "Bounded output queues with drop-oldest",
        "Dimension-aware buffer sizing"
    ) -InfluencesTtyHost yes
    # Release with detailed changelog
#>

param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("major", "minor", "patch")]
    [string]$Bump,

    [Parameter(Mandatory=$true)]
    [string]$Message,

    [Parameter(Mandatory=$false)]
    [string[]]$Details,

    [Parameter(Mandatory=$true)]
    [ValidateSet("yes", "no")]
    [string]$InfluencesTtyHost
)

$ErrorActionPreference = "Stop"

# Ensure we're up to date with remote
Write-Host "Checking remote status..." -ForegroundColor Cyan
git fetch origin 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: Could not fetch from remote" -ForegroundColor Yellow
}

$localCommit = git rev-parse HEAD 2>$null
$remoteCommit = git rev-parse origin/main 2>$null
$baseCommit = git merge-base HEAD origin/main 2>$null

if ($localCommit -ne $remoteCommit) {
    if ($baseCommit -eq $localCommit) {
        # Local is behind remote - need to pull
        Write-Host "Local branch is behind remote. Pulling changes..." -ForegroundColor Yellow
        git pull origin main 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "ERROR: Git pull failed - likely a merge conflict." -ForegroundColor Red
            Write-Host ""
            Write-Host "Please resolve manually:" -ForegroundColor Yellow
            Write-Host "  1. Run: git pull origin main" -ForegroundColor White
            Write-Host "  2. Resolve any merge conflicts" -ForegroundColor White
            Write-Host "  3. Run: git add . && git commit" -ForegroundColor White
            Write-Host "  4. Re-run this release script" -ForegroundColor White
            Write-Host ""
            exit 1
        }
        Write-Host "Pull successful." -ForegroundColor Green
    } elseif ($baseCommit -eq $remoteCommit) {
        # Local is ahead of remote - that's fine, we'll push
        Write-Host "Local branch is ahead of remote (will push new commits)." -ForegroundColor Gray
    } else {
        # Branches have diverged
        Write-Host ""
        Write-Host "ERROR: Local and remote branches have diverged." -ForegroundColor Red
        Write-Host ""
        Write-Host "Please resolve manually:" -ForegroundColor Yellow
        Write-Host "  1. Run: git pull origin main" -ForegroundColor White
        Write-Host "  2. Resolve any merge conflicts" -ForegroundColor White
        Write-Host "  3. Run: git add . && git commit" -ForegroundColor White
        Write-Host "  4. Re-run this release script" -ForegroundColor White
        Write-Host ""
        exit 1
    }
}

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

# Build commit message (supports multiline with -Details)
$commitMsg = "v${newVersion}: $Message"
if ($Details -and $Details.Count -gt 0) {
    $commitMsg += "`n`n"
    foreach ($detail in $Details) {
        $commitMsg += "- $detail`n"
    }
}

$commitMsg | git commit -F -
if ($LASTEXITCODE -ne 0) { throw "git commit failed" }

$commitMsg | git tag -a "v$newVersion" -F -
if ($LASTEXITCODE -ne 0) { throw "git tag failed" }

git push origin main
if ($LASTEXITCODE -ne 0) { throw "git push main failed" }

git push origin "v$newVersion"
if ($LASTEXITCODE -ne 0) { throw "git push tag failed" }

Write-Host ""
Write-Host "Released v$newVersion" -ForegroundColor Green
Write-Host "Monitor build: https://github.com/AiTlbx/MidTerm/actions" -ForegroundColor Cyan
