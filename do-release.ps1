Set-Location Q:\repos\MidTermWorkspace2
.\scripts\release.ps1 -Bump patch -ReleaseTitle "Fix AudioWorklet CSP blocking" -ReleaseNotes @("Added worker-src directive to Content-Security-Policy to allow AudioWorklet modules for voice feature") -mthostUpdate no
