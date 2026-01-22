Set-Location Q:\repos\MidTermWorkspace2
.\scripts\release.ps1 -Bump patch -ReleaseTitle "Dynamic voice server CSP based on request host" -ReleaseNotes @("Voice server CSP now uses request hostname instead of hardcoded localhost, enabling voice over Tailscale/remote access in dev mode") -mthostUpdate no
