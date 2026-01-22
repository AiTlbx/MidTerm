$dll = [System.Reflection.Assembly]::LoadFrom('Q:/repos/MidTermWorkspace2/src/Ai.Tlbx.MidTerm/bin/Debug/net10.0/mt.dll')
$dll.GetManifestResourceNames() | Where-Object { $_ -match 'audio' -or $_ -match 'webAudio' }
